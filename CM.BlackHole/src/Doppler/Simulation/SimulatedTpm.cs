using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;

namespace CM.BlackHole.Doppler.Simulation;

/// <summary>How a simulated machine boots: whether its firmware measures Secure Boot on, and what the agent reports.</summary>
public sealed record SimulatedMachine(bool SecureBoot, bool DiskEncrypted, string AgentPackageSha256);

/// <summary>
/// A software TPM 2.0 for the sandbox and CI (lane L4b goal 2: "the sandbox keeps the software path"). It produces the same TPM 2.0
/// structures a real TPM does, so Doppler has one check for both: an RSA 2048 endorsement key, a restricted RSA attestation key
/// (TPMT_PUBLIC), TPM2_ActivateCredential, TPM2_Certify and TPM2_Quote signed RSASSA SHA-256, and a crypto-agile event log whose PCR 7
/// events firmware would measure. The structures were checked against a real TPM 2.0 implementation (swtpm, libtpms): see
/// CM.BlackHole/tests/Doppler/Tpm/fixtures. Its keys are in memory: a software TPM is never evidence of a real machine, which is why
/// an enrolment plan names the endorsement key.
/// </summary>
public sealed class SimulatedTpm : IDeviceTpm, IDisposable
{
    const TpmObjectAttributes Bound = TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin | TpmObjectAttributes.UserWithAuth;

    readonly RSA endorsement = RSA.Create(2048);
    readonly RSA attestation = RSA.Create(2048);
    ECDsa? deviceKey;
    byte[]? devicePublic;

    /// <param name="machine">How the machine boots and what it reports.</param>
    /// <param name="attestationHash">The attestation key's signature hash: SHA-256, or SHA-1 as Windows' platform crypto provider makes it.</param>
    public SimulatedTpm(SimulatedMachine machine, ushort attestationHash = TpmAlg.Sha256)
    {
        Machine = machine;
        SignWith = attestationHash;
        AttestationKeyPublic = TpmPublic.EncodeRsa(Bound | TpmObjectAttributes.Restricted | TpmObjectAttributes.Sign, TpmAlg.RsaSsa, attestationHash,
            attestation.ExportParameters(false).Modulus!);
    }

    /// <summary>The hash the attestation key signs with; a test sets another one than the key's scheme names (a real TPM never does).</summary>
    public ushort SignWith { get; set; }

    /// <summary>How this machine boots and what it reports (a test makes one with Secure Boot off, for example).</summary>
    public SimulatedMachine Machine { get; set; }

    /// <summary>A test hook: extra PCR 7 events extended after boot (by software, after the separator).</summary>
    public List<(uint Type, byte[] Data)> ExtendedAfterBoot { get; } = [];

    public byte[] EndorsementKeySpki => endorsement.ExportSubjectPublicKeyInfo();

    /// <summary>The id an enrolment plan names this machine by.</summary>
    public string EndorsementKeyId => Convert.ToHexStringLower(SHA256.HashData(EndorsementKeySpki));

    public byte[] AttestationKeyPublic { get; }

    public byte[] AttestationKeyName => TpmPublic.Parse(AttestationKeyPublic).Name;

    public ReportedState Reported => new(Machine.DiskEncrypted, Machine.AgentPackageSha256);

    public byte[]? ActivateCredential(byte[] credentialBlob, byte[] encryptedSecret) =>
        TpmCredential.Activate(endorsement, AttestationKeyName, new TpmCredential.Sealed(credentialBlob, encryptedSecret));

    public byte[] CreateDeviceKey()
    {
        deviceKey?.Dispose();
        deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = deviceKey.ExportParameters(false).Q;
        devicePublic = TpmPublic.EncodeEccP256(Bound | TpmObjectAttributes.Sign, q.X!, q.Y!);
        return devicePublic;
    }

    public KeyAttestation CertifyDeviceKey(byte[] qualifyingData)
    {
        var publicArea = devicePublic ?? throw new InvalidOperationException("no device key yet");
        var attest = TpmAttest.EncodeCertify(AttestationKeyName, qualifyingData, TpmPublic.Parse(publicArea).Name);
        return new KeyAttestation(publicArea, attest, Sign(attest));
    }

    public PlatformQuote QuotePcr7(byte[] qualifyingData)
    {
        var log = TcgLog.Build(BootEvents().Select(e => (7, e.Type, e.Data)));
        var events = TcgLog.EventsOf(log, 7, TpmAlg.Sha256);
        var pcr = new byte[32];
        foreach (var e in events) pcr = SHA256.HashData([.. pcr, .. e.Digest]);
        var attest = TpmAttest.EncodeQuote(AttestationKeyName, qualifyingData, TpmAlg.Sha256, 7, SHA256.HashData(pcr));
        return new PlatformQuote(attest, Sign(attest), TpmAlg.Sha256, pcr, events);
    }

    /// <summary>What firmware measures into PCR 7: the Secure Boot state and its key databases, then the separator, then what the boot manager adds.</summary>
    IEnumerable<(uint Type, byte[] Data)> BootEvents()
    {
        var global = SecureBootPolicy.EfiGlobalVariable;
        yield return (TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(global, "SecureBoot", [Machine.SecureBoot ? (byte)1 : (byte)0]));
        yield return (TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(global, "PK", "sandbox-pk"u8.ToArray()));
        yield return (TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(global, "KEK", "sandbox-kek"u8.ToArray()));
        yield return (TcgEventType.Separator, new byte[4]);
        yield return (TcgEventType.EfiVariableAuthority, TcgLog.UefiVariable(global, "db", "sandbox-db-entry"u8.ToArray()));
        foreach (var extra in ExtendedAfterBoot) yield return extra;
    }

    public byte[] CreateSigningRequest()
    {
        var key = deviceKey ?? throw new InvalidOperationException("no device key yet");
        return new CertificateRequest("CN=enrolment", key, HashAlgorithmName.SHA256).CreateSigningRequest();
    }

    public IImSigner Bind(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        var key = deviceKey ?? throw new InvalidOperationException("no device key yet");
        return new EcdsaImSigner(certificate.CopyWithPrivateKey(key), chain);
    }

    public byte[] SignWithDeviceKey(byte[] data)
    {
        var key = deviceKey ?? throw new InvalidOperationException("no device key yet");
        return key.SignData(data, ImSignature.Hash, ImSignature.Format);
    }

    /// <summary>A test hook: an attestation-key signature over any bytes, as a TPM would never give for bytes it did not write.</summary>
    public byte[] SignAsAttestationKey(byte[] data) => Sign(data);

    byte[] Sign(byte[] attest) =>
        new TpmSignature(TpmAlg.RsaSsa, SignWith, attestation.SignData(attest, TpmAlg.HashName(SignWith), RSASignaturePadding.Pkcs1)).Encode();

    public void Dispose()
    {
        endorsement.Dispose();
        attestation.Dispose();
        deviceKey?.Dispose();
    }
}
