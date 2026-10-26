using System.Security.Cryptography.X509Certificates;
using Im.Trust;

namespace CM.BlackHole.Doppler;

/// <summary>
/// What the agent reports about the machine that the TPM does not measure: whether the system disk is encrypted, and the SHA-256 of
/// the agent package it runs. Reported, not attested: the TPM-held attestation key signs them inside the key certification (they are
/// in its qualifying data), so they cannot be changed on the way, but a compromised machine could report them falsely.
/// </summary>
public sealed record ReportedState(bool DiskEncrypted, string AgentPackageSha256);

/// <summary>Step 1 of an enrolment: the endorsement key (DER SubjectPublicKeyInfo) and the public area (TPMT_PUBLIC) of a fresh attestation key, under Doppler's challenge.</summary>
public sealed record EnrolBegin(byte[] Challenge, byte[] EndorsementKeySpki, byte[] AttestationKeyPublic);

/// <summary>Doppler's answer to step 1: a credential sealed to that endorsement key and attestation key (TPM2_MakeCredential), and the challenge for step 2.</summary>
public sealed record Activation(byte[] Challenge, byte[] CredentialBlob, byte[] EncryptedSecret);

/// <summary>The attestation key's TPM2_Certify of the device key: its public area, the signed TPMS_ATTEST and the TPMT_SIGNATURE.</summary>
public sealed record KeyAttestation(byte[] DeviceKeyPublic, byte[] Attest, byte[] Signature);

/// <summary>The attestation key's TPM2_Quote of PCR 7 in one bank, the PCR value, and the events that were extended into it.</summary>
public sealed record PlatformQuote(byte[] Attest, byte[] Signature, ushort Bank, byte[] Pcr7, IReadOnlyList<Tpm.PcrEvent> Pcr7Events);

/// <summary>Step 2: the recovered credential, the key certification, the quote, the reported state and a PKCS#10 request signed by the device key.</summary>
public sealed record EnrolFinish(byte[] Challenge, byte[] Credential, KeyAttestation Key, PlatformQuote Quote, ReportedState Reported, byte[] CertificateSigningRequest);

/// <summary>Why Doppler refused an enrolment or a token. Codes only; counted, never logged with content.</summary>
public enum DopplerRefusal
{
    None,
    UnknownChallenge,
    ExpiredChallenge,
    NoApprovedPlan,
    /// <summary>The endorsement key is not RSA 2048 or more, or the attestation key's public area is not a restricted, TPM-bound RSA signing key with SHA-256 names.</summary>
    BadAttestationKey,
    /// <summary>The credential sealed to the endorsement key and attestation key did not come back: the attestation key is not in that TPM.</summary>
    BadActivation,
    BadCertify,
    BadQuote,
    KeyMismatch,
    PolicyFailed,
    BadRequest,
    NotEnrolled,
    Revoked,
    BadProof,
    /// <summary>Seen by the agent only: the door gave no answer (Doppler refused without a reason, or the gate closed). Doppler's own count says why.</summary>
    Unanswered,
    /// <summary>Seen by the agent only: the enrolment answer does not hold up (GRB-0003 F3). The certificate does not chain to the agent's anchors in
    /// role Device, names another subject than the answer, or is not for the agent's device key. The agent stays unenrolled and tries again.</summary>
    BadAnswer,
}

public sealed record EnrolmentResult(DopplerRefusal Refusal, string? Subject, X509Certificate2? Certificate, IReadOnlyList<X509Certificate2> Chain)
{
    public bool Ok => Refusal == DopplerRefusal.None;

    public static EnrolmentResult Refused(DopplerRefusal refusal) => new(refusal, null, null, []);
}

/// <summary>Step 1's answer: the activation, or the refusal.</summary>
public sealed record BeginResult(DopplerRefusal Refusal, Activation? Activation)
{
    public bool Ok => Refusal == DopplerRefusal.None;
}

/// <summary>A token request: the enrolled subject proves possession of its device key by signing Doppler's fresh challenge.</summary>
public sealed record TokenRequest(string Subject, byte[] Challenge, byte[] Proof);

/// <summary>What Doppler hands back: the encoded token (Milky Way's format) and its opaque id.</summary>
public sealed record IssuedToken(string TokenId, byte[] Encoded, DateTimeOffset Expires);

public sealed record TokenResult(DopplerRefusal Refusal, IssuedToken? Token)
{
    public bool Ok => Refusal == DopplerRefusal.None;
}

/// <summary>
/// Everything Doppler needs to mint a token in Milky Way's format (DMP chapter 04, the token claims and codec in Im). The
/// token format is Milky Way's; Doppler decides who gets one, with which permissions, for how long, and signs with its authority key.
/// </summary>
public sealed record TokenClaims(
    string Subject,
    EnrolledRole Role,
    string DeviceKeyId,
    byte[] DeviceKeySpki,
    IReadOnlyList<string> Permissions,
    DateTimeOffset IssuedAt,
    DateTimeOffset Expires);

/// <summary>The seam to Milky Way's token codec. The composition root adapts Im's codec to it; tests may use any encoder.</summary>
public interface ITokenEncoder
{
    IssuedToken Encode(TokenClaims claims, IImSigner authority);
}

/// <summary>One audit line: who (the actor role or plan), what, which target, the outcome. Never a key, a token or message content.</summary>
public sealed record AuditLine(DateTimeOffset At, string Action, string Target, string Outcome);

public interface IDopplerAudit
{
    void Write(AuditLine line);
}

/// <summary>
/// An enrolment plan: the endorsement key of one machine and the role it may take, approved by admins (enrolment is a plan
/// with four eyes). In a deployment the plan comes from the administration tool's plan and gate; the endorsement key from the
/// vendor's registration of the device.
/// </summary>
public sealed record EnrolmentPlan(string PlanId, string EndorsementKeyId, EnrolledRole Role, IReadOnlyList<string> Approvers);
