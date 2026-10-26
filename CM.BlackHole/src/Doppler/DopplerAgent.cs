using System.Security.Cryptography.X509Certificates;
using Im.Trust;

namespace CM.BlackHole.Doppler;

public enum Transport
{
    Tcp,
    Udp,
}

/// <summary>The machine's network stack as the agent needs it.</summary>
public interface IDeviceHost
{
    bool TryBind(Transport transport, int port, string owner);
    string? OwnerOf(Transport transport, int port);
    void DisableNetBios();
}

/// <summary>
/// The machine's TPM as the agent needs it (lane L4b goal 2). On Windows it is the Platform Crypto Provider (keys created in the TPM,
/// non-exportable); in the sandbox a software TPM that produces the same TPM 2.0 structures, so Doppler has one check for both.
/// </summary>
public interface IDeviceTpm
{
    /// <summary>The endorsement key as DER SubjectPublicKeyInfo; its SHA-256 is the id an enrolment plan names the machine by.</summary>
    byte[] EndorsementKeySpki { get; }

    /// <summary>The attestation key's TPMT_PUBLIC (a restricted RSA signing key in the TPM, created on first use).</summary>
    byte[] AttestationKeyPublic { get; }

    /// <summary>TPM2_ActivateCredential: the credential Doppler sealed to this TPM's endorsement key and attestation key, or null.</summary>
    byte[]? ActivateCredential(byte[] credentialBlob, byte[] encryptedSecret);

    /// <summary>Creates a new non-exportable ECC P-256 device key in the TPM (replacing an earlier one) and returns its TPMT_PUBLIC.</summary>
    byte[] CreateDeviceKey();

    /// <summary>TPM2_Certify of the device key by the attestation key over <paramref name="qualifyingData"/>.</summary>
    KeyAttestation CertifyDeviceKey(byte[] qualifyingData);

    /// <summary>TPM2_Quote of PCR 7 (SHA-256 bank) by the attestation key over <paramref name="qualifyingData"/>, with PCR 7's events from the firmware's log.</summary>
    PlatformQuote QuotePcr7(byte[] qualifyingData);

    /// <summary>What the agent reports and the TPM does not measure (disk encryption, the agent package's hash).</summary>
    ReportedState Reported { get; }

    byte[] CreateSigningRequest();

    byte[] SignWithDeviceKey(byte[] data);

    IImSigner Bind(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain);
}

/// <summary>The agent's channel to Doppler. In Doppler's unit tests it is the server object; elsewhere the calls travel as IM through Milky Way's enrolment door (<see cref="DoorChannel"/>).</summary>
public interface IDopplerChannel
{
    /// <summary>A fresh challenge, or null when Doppler (or the door) gave none.</summary>
    byte[]? Challenge();
    BeginResult BeginEnrolment(EnrolBegin request);
    EnrolmentResult FinishEnrolment(EnrolFinish request);
    TokenResult IssueToken(TokenRequest request);
}

public enum AgentState
{
    Stopped,
    /// <summary>Started: port 138 is the agent's and NetBIOS is off.</summary>
    Started,
    /// <summary>Another process holds port 138; the agent does not run and says so.</summary>
    PortConflict,
    Enrolled,
    Refused,
}

/// <summary>
/// The Doppler agent on a machine enrolled into a role. It connects out only, owns TCP and UDP port 138 and switches NetBIOS
/// off (FCS-6), enrols through attestation, keeps its device key in the TPM, and fetches IM tokens from Doppler. It never accepts a
/// command to change its role; a new role is a new enrolment plan.
/// </summary>
/// <param name="answers">When given, the agent checks Doppler's enrolment answer before it believes it (GRB-0003 F3): the certificate must chain to
/// these anchors in role Device at <paramref name="time"/>'s now and name the subject the answer names. <see cref="AgentLoop"/> always passes them;
/// Doppler's own unit tests talk to the server object directly and leave them out.</param>
public sealed class DopplerAgent(IDeviceTpm tpm, IDeviceHost host, IDopplerChannel doppler, ITrustAnchors? answers = null, TimeProvider? time = null)
{
    public const string Owner = "doppler-agent";
    public const int ImPort = 138;

    public AgentState State { get; private set; } = AgentState.Stopped;
    public DopplerRefusal LastRefusal { get; private set; }
    public string? Subject { get; private set; }
    public IImSigner? DeviceSigner { get; private set; }
    public IssuedToken? Token { get; private set; }

    /// <summary>Takes port 138 (TCP and UDP) before anything else can, then switches NetBIOS off so its datagram service can never bind there again.</summary>
    public AgentState Start()
    {
        host.DisableNetBios();
        var tcp = host.TryBind(Transport.Tcp, ImPort, Owner);
        var udp = host.TryBind(Transport.Udp, ImPort, Owner);
        State = tcp && udp ? AgentState.Started : AgentState.PortConflict;
        return State;
    }

    /// <summary>
    /// Enrols in two steps: the attestation key is proved to live in the planned TPM (Doppler seals a credential to the endorsement
    /// key, the TPM opens it), then a new device key is certified by it and PCR 7 is quoted, both over Doppler's second challenge.
    /// </summary>
    public bool Enrol()
    {
        if (State is not (AgentState.Started or AgentState.Enrolled or AgentState.Refused)) return false;
        // The device key and its request are made before the door is used: the gate's handshake deadline covers the whole door
        // session, and key generation is the slowest TPM step.
        tpm.CreateDeviceKey();
        var csr = tpm.CreateSigningRequest();
        var reported = tpm.Reported;

        var challenge = doppler.Challenge();
        if (challenge is null) return Refused(DopplerRefusal.Unanswered);
        var begun = doppler.BeginEnrolment(new EnrolBegin(challenge, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic));
        if (!begun.Ok || begun.Activation is not { } activation) return Refused(begun.Refusal);
        var credential = tpm.ActivateCredential(activation.CredentialBlob, activation.EncryptedSecret);
        if (credential is null) return Refused(DopplerRefusal.BadActivation);

        var key = tpm.CertifyDeviceKey(EnrolmentStatements.CertifyData(activation.Challenge, csr, reported));
        var quote = tpm.QuotePcr7(EnrolmentStatements.QuoteData(activation.Challenge));
        var result = doppler.FinishEnrolment(new EnrolFinish(activation.Challenge, credential, key, quote, reported, csr));
        if (!result.Ok) return Refused(result.Refusal);
        if (result.Certificate is not { } certificate || string.IsNullOrWhiteSpace(result.Subject) || !Believable(result.Subject, certificate, result.Chain))
            return Refused(DopplerRefusal.BadAnswer);
        // The device key in the TPM must be the certificate's key; a certificate for another key is a bad answer, not a crash (GRB-0003 F6).
        if (TryBind(certificate, result.Chain) is not { } signer) return Refused(DopplerRefusal.BadAnswer);
        Subject = result.Subject;
        DeviceSigner = signer;
        State = AgentState.Enrolled;
        return true;
    }

    bool Believable(string subject, X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        if (answers is null) return true;
        // TrustAnchors.Validate refuses a certificate whose fields do not parse as Malformed; another ITrustAnchors may not, so the agent guards too.
        try { return Checks(subject, certificate, chain); }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    bool Checks(string subject, X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        if (!answers!.Validate(certificate, chain ?? [], TrustRole.Device, (time ?? TimeProvider.System).GetUtcNow()).Ok) return false;
        return certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false) == subject;
    }

    IImSigner? TryBind(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        // CopyWithPrivateKey throws ArgumentException for a key of another algorithm or value, CryptographicException for a certificate it cannot use.
        try { return tpm.Bind(certificate, chain ?? []); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException) { return null; }
    }

    /// <summary>
    /// After a restart: the machine is enrolled as <paramref name="subject"/> with the Device certificate the agent kept, bound to the
    /// device key still in the TPM. Doppler is not asked; if the register says otherwise, the next token request is refused.
    /// </summary>
    public bool Restore(string subject, X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (State != AgentState.Started) return false;
        // A kept record is checked like a fresh answer: a foreign or unusable certificate means "not enrolled", and the next round enrols again.
        if (!Believable(subject, certificate, chain) || TryBind(certificate, chain) is not { } signer) return false;
        DeviceSigner = signer;
        Subject = subject;
        State = AgentState.Enrolled;
        return true;
    }

    public bool RefreshToken()
    {
        if (State != AgentState.Enrolled || Subject is null) return false;
        var challenge = doppler.Challenge();
        if (challenge is null)
        {
            LastRefusal = DopplerRefusal.Unanswered;
            return false;
        }
        var proof = tpm.SignWithDeviceKey(EnrolmentStatements.TokenProof(Subject, challenge));
        var result = doppler.IssueToken(new TokenRequest(Subject, challenge, proof));
        LastRefusal = result.Refusal;
        if (!result.Ok) return false;
        Token = result.Token;
        return true;
    }

    bool Refused(DopplerRefusal refusal)
    {
        LastRefusal = refusal;
        State = AgentState.Refused;
        return false;
    }
}

/// <summary>Adapts a <see cref="DopplerServer"/> to the agent's channel in the sandbox.</summary>
public sealed class DirectChannel(DopplerServer server) : IDopplerChannel
{
    public byte[]? Challenge() => server.Challenge();
    public BeginResult BeginEnrolment(EnrolBegin request) => server.BeginEnrolment(request);
    public EnrolmentResult FinishEnrolment(EnrolFinish request) => server.FinishEnrolment(request);
    public TokenResult IssueToken(TokenRequest request) => server.IssueToken(request);
}
