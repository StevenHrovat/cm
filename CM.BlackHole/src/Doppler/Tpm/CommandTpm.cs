using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Im.Trust;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>
/// An ECDSA P-256 key that lives in a TPM: signing is TPM2_Sign, the private key never reaches this process. It lets .NET's own
/// certificate request and signature code use a TPM key (<see cref="CertificateRequest"/> signs through <see cref="SignHash"/>).
/// </summary>
public sealed class TpmEcdsa : ECDsa
{
    readonly Func<byte[], byte[]> signDigest;
    readonly ECParameters publicKey;

    /// <param name="signDigest">Signs a SHA-256 digest in the TPM and returns r and s (IEEE P1363).</param>
    /// <param name="publicKey">The key's public point on NIST P-256.</param>
    public TpmEcdsa(Func<byte[], byte[]> signDigest, ECParameters publicKey)
    {
        this.signDigest = signDigest ?? throw new ArgumentNullException(nameof(signDigest));
        this.publicKey = publicKey;
        KeySizeValue = 256;
        LegalKeySizesValue = [new KeySizes(256, 256, 0)];
    }

    public static ECParameters P256(byte[] x, byte[] y) => new() { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } };

    public override byte[] SignHash(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 32) throw new CryptographicException("the TPM device key signs SHA-256 digests only");
        return signDigest(hash);
    }

    public override bool VerifyHash(byte[] hash, byte[] signature)
    {
        using var verifier = ECDsa.Create(publicKey);
        return verifier.VerifyHash(hash, signature);
    }

    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters) throw new CryptographicException("a TPM key cannot be exported");
        return publicKey;
    }

    public override ECParameters ExportExplicitParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters) throw new CryptographicException("a TPM key cannot be exported");
        using var copy = ECDsa.Create(publicKey);
        return copy.ExportExplicitParameters(false);
    }

    public override void ImportParameters(ECParameters parameters) => throw new NotSupportedException("a TPM key is created in the TPM");

    public override void GenerateKey(ECCurve curve) => throw new NotSupportedException("a TPM key is created in the TPM");
}

/// <summary>An IM signer over any ECDSA P-256 key object (a TPM key through <see cref="TpmEcdsa"/>, or the platform provider's CNG key).</summary>
public sealed class KeyImSigner : IImSigner
{
    readonly ECDsa key;

    public KeyImSigner(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(key);
        if (key.KeySize != 256) throw new ArgumentException("IM version 0 signs with ECDSA P-256 only", nameof(key));
        if (!certificate.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo()))
            throw new ArgumentException("the certificate is not for this key", nameof(certificate));
        this.key = key;
        Certificate = certificate;
        Chain = chain;
        KeyId = ImSignature.KeyIdOf(certificate);
    }

    public string KeyId { get; }
    public X509Certificate2 Certificate { get; }
    public IReadOnlyList<X509Certificate2> Chain { get; }

    public byte[] Sign(ReadOnlySpan<byte> data) => key.SignData(data, ImSignature.Hash, ImSignature.Format);
}

/// <summary>
/// The agent's TPM through plain TPM 2.0 commands (<see cref="TpmCommands"/>), for a TPM the agent may command directly: swtpm in
/// CI, a Linux resource manager. The endorsement key is the TCG default template, the attestation key a primary of the endorsement
/// hierarchy, the device key a primary of the owner hierarchy chosen by a stored unique (not secret), so nothing secret is kept outside
/// the TPM. PCR 7's events come from the machine's TCG log (<paramref name="tcgLog"/>). On Windows the platform crypto provider owns
/// the keys instead (<c>PcpTpm</c>), and only the certify and the quote use these commands.
/// </summary>
public sealed class CommandTpm : IDeviceTpm, IDisposable
{
    readonly TpmCommands tpm;
    readonly Func<byte[]> tcgLog;
    readonly Func<ReportedState> reported;
    readonly TpmLoadedKey endorsement;
    readonly TpmLoadedKey attestation;
    TpmLoadedKey? device;
    TpmEcdsa? deviceKey;

    public CommandTpm(ITpmTransport transport, Func<byte[]> tcgLog, Func<ReportedState> reported, byte[]? deviceKeyUnique = null)
    {
        tpm = new TpmCommands(transport);
        this.tcgLog = tcgLog ?? throw new ArgumentNullException(nameof(tcgLog));
        this.reported = reported ?? throw new ArgumentNullException(nameof(reported));
        endorsement = tpm.CreatePrimary(TpmHandle.Endorsement, TpmCommands.EndorsementKeyTemplate());
        attestation = tpm.CreatePrimary(TpmHandle.Endorsement, TpmCommands.AttestationKeyTemplate());
        EndorsementKeySpki = EndorsementSpkiOf(endorsement.Parsed);
        if (deviceKeyUnique is not null) LoadDeviceKey(deviceKeyUnique);
    }

    /// <summary>The commands, for the diagnose output and tests.</summary>
    public TpmCommands Commands => tpm;

    public byte[] EndorsementKeySpki { get; }

    public string EndorsementKeyId => Convert.ToHexStringLower(SHA256.HashData(EndorsementKeySpki));

    public byte[] AttestationKeyPublic => attestation.Public;

    /// <summary>The TPM's response code of the last command that failed inside a method that answers null (for the diagnose output).</summary>
    public uint? LastError { get; private set; }

    /// <summary>What picks the device key in the owner hierarchy; the agent stores it to find the same key after a restart.</summary>
    public byte[]? DeviceKeyUnique { get; private set; }

    public ReportedState Reported => reported();

    public static byte[] EndorsementSpkiOf(TpmPublic ek)
    {
        ArgumentNullException.ThrowIfNull(ek);
        using var rsa = ek.RsaKey();
        return rsa.ExportSubjectPublicKeyInfo();
    }

    public byte[]? ActivateCredential(byte[] credentialBlob, byte[] encryptedSecret)
    {
        try { return tpm.ActivateCredential(attestation.Handle, endorsement.Handle, Sized(credentialBlob), Sized(encryptedSecret)); }
        catch (TpmCommandException e) { LastError = e.ResponseCode | (e.CommandCode << 16); return null; }
        catch (TpmFormatException) { return null; }
    }

    public byte[] CreateDeviceKey()
    {
        LoadDeviceKey(RandomNumberGenerator.GetBytes(32));
        return device!.Public;
    }

    public void LoadDeviceKey(byte[] unique)
    {
        if (device is not null) tpm.TryFlush(device.Handle);
        deviceKey?.Dispose();
        device = tpm.CreatePrimary(TpmHandle.Owner, TpmCommands.DeviceKeyTemplate(unique));
        DeviceKeyUnique = unique;
        var parsed = device.Parsed;
        var handle = device.Handle;
        deviceKey = new TpmEcdsa(digest => tpm.SignEcdsaP256(handle, digest), TpmEcdsa.P256(parsed.EccX, parsed.EccY));
    }

    public KeyAttestation CertifyDeviceKey(byte[] qualifyingData)
    {
        var key = device ?? throw new InvalidOperationException("no device key yet");
        var (attest, signature) = tpm.Certify(key.Handle, attestation.Handle, qualifyingData);
        return new KeyAttestation(key.Public, attest, signature);
    }

    public PlatformQuote QuotePcr7(byte[] qualifyingData)
    {
        var (attest, signature) = tpm.Quote(attestation.Handle, qualifyingData, TpmAlg.Sha256, [7]);
        var pcr7 = tpm.PcrRead(TpmAlg.Sha256, 7);
        var events = TcgLog.EventsOf(tcgLog(), 7, TpmAlg.Sha256);
        return new PlatformQuote(attest, signature, TpmAlg.Sha256, pcr7, events);
    }

    public byte[] CreateSigningRequest() =>
        new CertificateRequest("CN=enrolment", DeviceKey, HashAlgorithmName.SHA256).CreateSigningRequest();

    public byte[] SignWithDeviceKey(byte[] data) => DeviceKey.SignData(data, ImSignature.Hash, ImSignature.Format);

    public IImSigner Bind(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain) => new KeyImSigner(certificate, chain, DeviceKey);

    TpmEcdsa DeviceKey => deviceKey ?? throw new InvalidOperationException("no device key yet");

    /// <summary>Doppler sends the TPM2B forms (size included); the command wants the contents.</summary>
    static byte[] Sized(byte[] tpm2b)
    {
        var r = new TpmReader(tpm2b);
        var inner = r.Sized(1024);
        r.End();
        return inner;
    }

    public void Dispose()
    {
        deviceKey?.Dispose();
        if (device is not null) tpm.TryFlush(device.Handle);
        tpm.TryFlush(attestation.Handle);
        tpm.TryFlush(endorsement.Handle);
    }
}
