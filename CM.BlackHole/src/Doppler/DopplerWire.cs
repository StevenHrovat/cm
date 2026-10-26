using System.Security.Cryptography.X509Certificates;
using Im;

namespace CM.BlackHole.Doppler;

/// <summary>What a door payload asks Doppler for (DMP chapter 04, "The enrolment door"; the payloads are Doppler's, Milky Way never reads them).</summary>
public enum DoorOp : byte
{
    Challenge = 1,
    EnrolBegin = 2,
    EnrolFinish = 3,
    Token = 4,
}

/// <summary>A parsed door request: exactly one of the bodies is set, as <see cref="Op"/> says.</summary>
public sealed record DoorRequest(DoorOp Op, EnrolBegin? Begin, EnrolFinish? Finish, TokenRequest? Token);

/// <summary>
/// The canonical bytes of Doppler's door payloads, in the encoding rules of DMP chapter 04 (big-endian, length-prefixed, strict,
/// nothing trailing), with the IM writer and reader of <c>Im</c>. A request starts with <c>"DPQ"</c> and version 0, an answer
/// with <c>"DPA"</c> and version 0, then the operation byte. Doppler refuses by answering nothing (the gate sends refused), so there
/// is no refusal answer and no reason on the wire. Every parser throws <see cref="ImFormatException"/> on anything else.
/// </summary>
public static class DopplerWire
{
    public const string RequestMagic = "DPQ";
    public const string AnswerMagic = "DPA";
    public const byte Version = 0;

    public const int ChallengeLength = 32;
    public const int SpkiMax = 1024;
    public const int SignatureMax = 512;
    public const int CsrMax = 4096;
    public const int PackageHashLength = 64;
    public const int TpmPublicMax = 1024;
    public const int AttestMax = 1024;
    public const int CredentialMax = 64;
    public const int DigestMax = 64;
    public const int EventsMax = 512;

    // ---- requests ---------------------------------------------------------------------------------------------------

    public static byte[] ChallengeRequest() => new ImWriter().Magic(RequestMagic, Version).U8((byte)DoorOp.Challenge).ToArray();

    public static byte[] BeginRequest(EnrolBegin request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ImWriter().Magic(RequestMagic, Version).U8((byte)DoorOp.EnrolBegin)
            .Fixed(request.Challenge, ChallengeLength)
            .Bytes16(request.EndorsementKeySpki, SpkiMax)
            .Bytes16(request.AttestationKeyPublic, TpmPublicMax)
            .ToArray();
    }

    public static byte[] FinishRequest(EnrolFinish request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var w = new ImWriter().Magic(RequestMagic, Version).U8((byte)DoorOp.EnrolFinish)
            .Fixed(request.Challenge, ChallengeLength)
            .Bytes16(request.Credential, CredentialMax)
            .Bytes16(request.Key.DeviceKeyPublic, TpmPublicMax)
            .Bytes16(request.Key.Attest, AttestMax)
            .Bytes16(request.Key.Signature, SignatureMax)
            .Bytes16(request.Quote.Attest, AttestMax)
            .Bytes16(request.Quote.Signature, SignatureMax)
            .U16(request.Quote.Bank)
            .Bytes16(request.Quote.Pcr7, DigestMax);
        if (request.Quote.Pcr7Events.Count > EventsMax) throw new ArgumentException("too many PCR 7 events", nameof(request));
        w.U16((ushort)request.Quote.Pcr7Events.Count);
        foreach (var e in request.Quote.Pcr7Events) w.U32(e.Type).Bytes16(e.Digest, DigestMax).Bytes32(e.Data, ImLimits.DoorMax);
        return w.U8(request.Reported.DiskEncrypted ? (byte)1 : (byte)0)
            .Id8(request.Reported.AgentPackageSha256, ImAlphabet.Name, PackageHashLength)
            .Bytes16(request.CertificateSigningRequest, CsrMax)
            .ToArray();
    }

    public static byte[] TokenRequest(TokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ImWriter().Magic(RequestMagic, Version).U8((byte)DoorOp.Token)
            .Id8(request.Subject, ImAlphabet.Subject, ImLimits.SubjectMax)
            .Fixed(request.Challenge, ChallengeLength)
            .Bytes16(request.Proof, SignatureMax)
            .ToArray();
    }

    public static DoorRequest ParseRequest(ReadOnlyMemory<byte> payload)
    {
        var r = new ImReader(payload);
        r.Magic(RequestMagic, Version);
        var op = (DoorOp)r.U8();
        DoorRequest request;
        switch (op)
        {
            case DoorOp.Challenge:
                request = new DoorRequest(op, null, null, null);
                break;
            case DoorOp.EnrolBegin:
                request = new DoorRequest(op, new EnrolBegin(r.Fixed(ChallengeLength), r.Bytes16(SpkiMax), r.Bytes16(TpmPublicMax)), null, null);
                break;
            case DoorOp.EnrolFinish:
                var challenge = r.Fixed(ChallengeLength);
                var credential = r.Bytes16(CredentialMax);
                var key = new KeyAttestation(r.Bytes16(TpmPublicMax), r.Bytes16(AttestMax), r.Bytes16(SignatureMax));
                var quoteAttest = r.Bytes16(AttestMax);
                var quoteSignature = r.Bytes16(SignatureMax);
                var bank = r.U16();
                var pcr7 = r.Bytes16(DigestMax);
                var count = r.U16();
                if (count > EventsMax) throw new ImFormatException(ImFormatError.Malformed);
                var events = new List<Tpm.PcrEvent>(count);
                for (var i = 0; i < count; i++) events.Add(new Tpm.PcrEvent(r.U32(), r.Bytes16(DigestMax), r.Bytes32(ImLimits.DoorMax)));
                var disk = r.U8();
                if (disk > 1) throw new ImFormatException(ImFormatError.Malformed);
                var reported = new ReportedState(disk == 1, r.Id8(ImAlphabet.Name, PackageHashLength));
                var csr = r.Bytes16(CsrMax);
                request = new DoorRequest(op, null, new EnrolFinish(challenge, credential, key, new PlatformQuote(quoteAttest, quoteSignature, bank, pcr7, events), reported, csr), null);
                break;
            case DoorOp.Token:
                request = new DoorRequest(op, null, null, new TokenRequest(r.Id8(ImAlphabet.Subject, ImLimits.SubjectMax), r.Fixed(ChallengeLength), r.Bytes16(SignatureMax)));
                break;
            default:
                throw new ImFormatException(ImFormatError.Malformed);
        }
        r.End();
        return request;
    }

    // ---- answers ----------------------------------------------------------------------------------------------------

    public static byte[] ChallengeAnswer(byte[] challenge) =>
        new ImWriter().Magic(AnswerMagic, Version).U8((byte)DoorOp.Challenge).Fixed(challenge, ChallengeLength).ToArray();

    public static byte[] ActivationAnswer(Activation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        return new ImWriter().Magic(AnswerMagic, Version).U8((byte)DoorOp.EnrolBegin)
            .Fixed(activation.Challenge, ChallengeLength)
            .Bytes16(activation.CredentialBlob, 1024)
            .Bytes16(activation.EncryptedSecret, 1024)
            .ToArray();
    }

    public static Activation ParseActivationAnswer(ReadOnlyMemory<byte> payload)
    {
        var r = Answer(payload, DoorOp.EnrolBegin);
        var activation = new Activation(r.Fixed(ChallengeLength), r.Bytes16(1024), r.Bytes16(1024));
        r.End();
        return activation;
    }

    public static byte[] EnrolAnswer(EnrolmentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Ok || result.Subject is null || result.Certificate is null) throw new ArgumentException("only an accepted enrolment has an answer", nameof(result));
        return new ImWriter().Magic(AnswerMagic, Version).U8((byte)DoorOp.EnrolFinish)
            .Id8(result.Subject, ImAlphabet.Subject, ImLimits.SubjectMax)
            .Bytes16(result.Certificate.RawData, ImLimits.CertificateMax)
            .List16([.. result.Chain.Select(c => c.RawData)], ImLimits.ChainMax, ImLimits.CertificateMax)
            .ToArray();
    }

    public static byte[] TokenAnswer(IssuedToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ImWriter().Magic(AnswerMagic, Version).U8((byte)DoorOp.Token)
            .Bytes32(token.Encoded, ImLimits.TokenMax + 4 + ImLimits.SignatureLength)
            .I64(ImTime.ToWire(token.Expires))
            .ToArray();
    }

    public static byte[] ParseChallengeAnswer(ReadOnlyMemory<byte> payload)
    {
        var r = Answer(payload, DoorOp.Challenge);
        var challenge = r.Fixed(ChallengeLength);
        r.End();
        return challenge;
    }

    /// <summary>The subject and the certificate chain of an accepted enrolment. The caller disposes the certificates.</summary>
    public static (string Subject, X509Certificate2 Certificate, IReadOnlyList<X509Certificate2> Chain) ParseEnrolAnswer(ReadOnlyMemory<byte> payload)
    {
        var r = Answer(payload, DoorOp.EnrolFinish);
        var subject = r.Id8(ImAlphabet.Subject, ImLimits.SubjectMax);
        var certificate = r.Bytes16(ImLimits.CertificateMax);
        var chain = r.List16(ImLimits.ChainMax, ImLimits.CertificateMax);
        r.End();
        try
        {
            return (subject, X509CertificateLoader.LoadCertificate(certificate), [.. chain.Select(c => X509CertificateLoader.LoadCertificate(c))]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new ImFormatException(ImFormatError.Malformed);
        }
    }

    /// <summary>The encoded token (Milky Way's format, unread here) and when it expires.</summary>
    public static (byte[] Token, DateTimeOffset Expires) ParseTokenAnswer(ReadOnlyMemory<byte> payload)
    {
        var r = Answer(payload, DoorOp.Token);
        var token = r.Bytes32(ImLimits.TokenMax + 4 + ImLimits.SignatureLength);
        var expires = ImTime.FromWire(r.I64());
        r.End();
        return (token, expires);
    }

    static ImReader Answer(ReadOnlyMemory<byte> payload, DoorOp expected)
    {
        var r = new ImReader(payload);
        r.Magic(AnswerMagic, Version);
        if (r.U8() != (byte)expected) throw new ImFormatException(ImFormatError.Malformed);
        return r;
    }

}
