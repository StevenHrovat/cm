using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;

namespace CM.BlackHole.Doppler.Windows;

/// <summary>
/// The agent's TPM on Windows (lane L4b goal 2): the keys belong to the Windows Platform Crypto Provider, so they are created in the TPM
/// and cannot be exported; the attestation key is a provider identity key (restricted signing), the device key a provider ECDSA P-256
/// key, which Windows' TLS stack can also use as the client certificate's key. Only TPM2_Certify, TPM2_Quote, TPM2_ReadPublic and
/// TPM2_PCR_Read go to the TPM directly, through the provider's own TBS context with the provider's handles of its keys (the way
/// go-attestation does it), so the certify and the quote are the plain TPM 2.0 structures Doppler checks. PCR 7's events come from the
/// firmware's TCG log of this boot (Tbsi_Get_TCG_Log). The agent runs as LocalSystem, so the keys are LocalSystem's.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PcpTpm : IDeviceTpm, IDisposable
{
    readonly IntPtr provider;
    readonly IntPtr attestation;
    readonly TpmCommands tpm;
    readonly Func<ReportedState> reported;
    readonly string deviceKeyName;
    IntPtr device;
    ECDsaCng? deviceKey;

    /// <param name="reported">What the agent reports (disk encryption, its package hash).</param>
    /// <param name="keyPrefix">The provider key names are <c>{prefix}-aik</c> and <c>{prefix}-device</c>; the diagnose command uses its own prefix so it never touches the enrolled keys.</param>
    public PcpTpm(Func<ReportedState> reported, string keyPrefix = "cm-doppler")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        this.reported = reported ?? throw new ArgumentNullException(nameof(reported));
        deviceKeyName = keyPrefix + "-device";
        PcpNative.Check(PcpNative.NCryptOpenStorageProvider(out provider, PcpNative.ProviderName, 0), "NCryptOpenStorageProvider");
        EndorsementKeySpki = PcpBlobs.SpkiFromRsaPublicBlob(PcpNative.GetProperty(provider, "PCP_EKPUB"));
        tpm = new TpmCommands(new TbsTransport(PlatformContext(provider)));
        attestation = OpenOrCreateAttestationKey(keyPrefix + "-aik");
        AttestationKeyPublic = tpm.ReadPublic(TpmHandleOf(attestation)).Public;
        if (PcpNative.NCryptOpenKey(provider, out var existing, deviceKeyName, 0, 0) == 0) UseDeviceKey(existing);
    }

    public byte[] EndorsementKeySpki { get; }

    public string EndorsementKeyId => Convert.ToHexStringLower(SHA256.HashData(EndorsementKeySpki));

    public byte[] AttestationKeyPublic { get; }

    public ReportedState Reported => reported();

    /// <summary>True when a device key from an earlier enrolment exists (the agent keeps using it after a restart).</summary>
    public bool HasDeviceKey => deviceKey is not null;

    /// <summary>The provider's status of the last call that failed inside a method that answers null (for the diagnose output).</summary>
    public int? LastError { get; private set; }

    public byte[]? ActivateCredential(byte[] credentialBlob, byte[] encryptedSecret)
    {
        try
        {
            PcpNative.SetProperty(attestation, "PCP_TPM12_IDACTIVATION", PcpBlobs.ActivationBlob(credentialBlob, encryptedSecret));
            return PcpNative.GetProperty(attestation, "PCP_TPM12_IDACTIVATION");
        }
        catch (PcpException e)
        {
            LastError = e.Status;
            return null;
        }
    }

    public byte[] CreateDeviceKey()
    {
        if (deviceKey is not null)
        {
            deviceKey.Dispose();
            deviceKey = null;
            if (PcpNative.NCryptDeleteKey(device, 0) != 0) PcpNative.NCryptFreeObject(device);
            device = IntPtr.Zero;
        }
        else if (PcpNative.NCryptOpenKey(provider, out var stale, deviceKeyName, 0, 0) == 0 && PcpNative.NCryptDeleteKey(stale, 0) != 0)
        {
            PcpNative.NCryptFreeObject(stale);
        }
        PcpNative.Check(PcpNative.NCryptCreatePersistedKey(provider, out var created, "ECDSA_P256", deviceKeyName, 0, 0), "NCryptCreatePersistedKey device");
        PcpNative.Check(PcpNative.NCryptFinalizeKey(created, 0), "NCryptFinalizeKey device");
        UseDeviceKey(created);
        return tpm.ReadPublic(TpmHandleOf(device)).Public;
    }

    public KeyAttestation CertifyDeviceKey(byte[] qualifyingData)
    {
        if (deviceKey is null) throw new InvalidOperationException("no device key yet");
        var deviceHandle = TpmHandleOf(device);
        var aik = TpmHandleOf(attestation);
        var publicArea = tpm.ReadPublic(deviceHandle).Public;
        (byte[] Attest, byte[] Signature) certified;
        // The provider's identity keys name their scheme (RSASSA with SHA-1 on the Windows versions go-attestation supports); a key
        // without one needs the scheme spelled out, so the null scheme is tried first and RSASSA SHA-1 second.
        try { certified = tpm.Certify(deviceHandle, aik, qualifyingData); }
        catch (TpmCommandException) { certified = tpm.Certify(deviceHandle, aik, qualifyingData, TpmAlg.RsaSsa, TpmAlg.Sha1); }
        return new KeyAttestation(publicArea, certified.Attest, certified.Signature);
    }

    public PlatformQuote QuotePcr7(byte[] qualifyingData)
    {
        var aik = TpmHandleOf(attestation);
        (byte[] Attest, byte[] Signature) quoted;
        try { quoted = tpm.Quote(aik, qualifyingData, TpmAlg.Sha256, [7]); }
        catch (TpmCommandException) { quoted = tpm.Quote(aik, qualifyingData, TpmAlg.Sha256, [7], TpmAlg.RsaSsa, TpmAlg.Sha1); }
        var pcr7 = tpm.PcrRead(TpmAlg.Sha256, 7);
        var events = TcgLog.EventsOf(PcpNative.TcgLog(), 7, TpmAlg.Sha256);
        return new PlatformQuote(quoted.Attest, quoted.Signature, TpmAlg.Sha256, pcr7, events);
    }

    public byte[] CreateSigningRequest() =>
        new CertificateRequest("CN=enrolment", DeviceKey, HashAlgorithmName.SHA256).CreateSigningRequest();

    public byte[] SignWithDeviceKey(byte[] data) => DeviceKey.SignData(data, ImSignature.Hash, ImSignature.Format);

    public IImSigner Bind(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain) => new KeyImSigner(certificate, chain, DeviceKey);

    /// <summary>The Device certificate joined to the TPM key, for the TLS client certificate (Windows' TLS stack signs through the provider).</summary>
    public X509Certificate2 TlsCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.CopyWithPrivateKey(DeviceKey);
    }

    ECDsaCng DeviceKey => deviceKey ?? throw new InvalidOperationException("no device key yet");

    /// <summary>Deletes the provider keys of <paramref name="keyPrefix"/> (the uninstaller's "forget"); true for each key that existed and is gone.</summary>
    public static (bool AttestationKey, bool DeviceKey) DeleteKeys(string keyPrefix = "cm-doppler")
    {
        PcpNative.Check(PcpNative.NCryptOpenStorageProvider(out var provider, PcpNative.ProviderName, 0), "NCryptOpenStorageProvider");
        try { return (Delete(provider, keyPrefix + "-aik"), Delete(provider, keyPrefix + "-device")); }
        finally { PcpNative.NCryptFreeObject(provider); }

        static bool Delete(IntPtr provider, string name)
        {
            if (PcpNative.NCryptOpenKey(provider, out var key, name, 0, 0) != 0) return false;
            if (PcpNative.NCryptDeleteKey(key, 0) == 0) return true;
            PcpNative.NCryptFreeObject(key);
            return false;
        }
    }

    void UseDeviceKey(IntPtr handle)
    {
        device = handle;
        deviceKey = new ECDsaCng(CngKey.Open(deviceKeyName, new CngProvider(PcpNative.ProviderName)));
    }

    IntPtr OpenOrCreateAttestationKey(string name)
    {
        var status = PcpNative.NCryptOpenKey(provider, out var key, name, 0, 0);
        if (status == 0) return key;
        if (status is not (PcpNative.NteBadKeyset or PcpNative.NteNotFound)) throw new PcpException(status, "NCryptOpenKey aik");
        PcpNative.Check(PcpNative.NCryptCreatePersistedKey(provider, out key, "RSA", name, 0, 0), "NCryptCreatePersistedKey aik");
        PcpNative.SetProperty(key, "Length", U32(2048));
        PcpNative.SetProperty(key, "PCP_KEY_USAGE_POLICY", U32(PcpNative.IdentityKeyUsage));
        PcpNative.Check(PcpNative.NCryptFinalizeKey(key, 0), "NCryptFinalizeKey aik");
        return key;
    }

    static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>The TPM handle the provider uses for one of its keys (<c>PCP_PLATFORMHANDLE</c> on a key).</summary>
    static uint TpmHandleOf(IntPtr key) => BinaryPrimitives.ReadUInt32LittleEndian(PcpNative.GetProperty(key, "PCP_PLATFORMHANDLE"));

    /// <summary>The provider's TBS context (<c>PCP_PLATFORMHANDLE</c> on the provider), owned and closed by the provider.</summary>
    static IntPtr PlatformContext(IntPtr provider)
    {
        var bytes = PcpNative.GetProperty(provider, "PCP_PLATFORMHANDLE");
        return bytes.Length == 8 ? (IntPtr)BinaryPrimitives.ReadInt64LittleEndian(bytes) : (IntPtr)BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public void Dispose()
    {
        deviceKey?.Dispose();
        if (device != IntPtr.Zero) PcpNative.NCryptFreeObject(device);
        if (attestation != IntPtr.Zero) PcpNative.NCryptFreeObject(attestation);
        if (provider != IntPtr.Zero) PcpNative.NCryptFreeObject(provider);
    }
}
