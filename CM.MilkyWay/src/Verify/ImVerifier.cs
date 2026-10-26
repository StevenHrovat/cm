using System.Security.Cryptography;
using Im;
using Im.Trust;

namespace CM.MilkyWay.Verify;

/// <summary>
/// A message that passed every verify rule. Only <see cref="ImVerifier"/> can create one (the constructor is internal), and every
/// consumer takes this type, so an unverified message cannot reach a consumer: the compiler refuses it (DMP chapter 05, "The verifier").
/// </summary>
public sealed class VerifiedMessage
{
    internal VerifiedMessage(string subject, EnrolledRole role, string subjectKeyId, string tokenId, string kind, byte[] messageId, DateTimeOffset sentAt, DateTimeOffset receivedAt, ImHeartbeat? heartbeat)
    {
        Subject = subject;
        Role = role;
        SubjectKeyId = subjectKeyId;
        TokenId = tokenId;
        Kind = kind;
        MessageId = messageId;
        SentAt = sentAt;
        ReceivedAt = receivedAt;
        Heartbeat = heartbeat;
    }

    /// <summary>The opaque subject Doppler assigned.</summary>
    public string Subject { get; }

    public EnrolledRole Role { get; }

    public string SubjectKeyId { get; }

    /// <summary>Lower-case hex of the token id the message carried. Milky Way's health report lists the ids it accepted, so Doppler can raise an alarm for any id it never issued.</summary>
    public string TokenId { get; }

    public string Kind { get; }

    public byte[] MessageId { get; }

    public DateTimeOffset SentAt { get; }

    public DateTimeOffset ReceivedAt { get; }

    /// <summary>The payload when <see cref="Kind"/> is <see cref="ImKinds.Heartbeat"/>.</summary>
    public ImHeartbeat? Heartbeat { get; }
}

/// <summary>The answer of the verifier: a verified message or a refusal, never both.</summary>
public readonly record struct VerifyResult(VerifiedMessage? Message, ImRefusal? Refusal)
{
    public bool IsVerified => Message is not null;

    internal static VerifyResult Refuse(ImRefusal reason) => new(null, reason);
}

/// <summary>
/// The IM verifier (DMP chapter 04, "The verify rules"). It checks every message by rules 1 to 16 in their order, stops at the first
/// that fails and fails closed. Time comes only from the injected <see cref="TimeProvider"/>, trust only from
/// <see cref="ITrustAnchors"/>, enrolment only from Doppler's <see cref="IEnrolmentRegistry"/>. There is no switch that skips a rule.
/// </summary>
public sealed class ImVerifier
{
    readonly MilkyWaySettings settings;
    readonly ITrustAnchors trust;
    readonly IEnrolmentRegistry enrolment;
    readonly TimeProvider time;
    readonly IImRefusalLog log;
    readonly ReplayMemory replay;

    public ImVerifier(MilkyWaySettings settings, ITrustAnchors trust, IEnrolmentRegistry enrolment, TimeProvider time, IImRefusalLog log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.settings = settings;
        this.trust = trust ?? throw new ArgumentNullException(nameof(trust));
        this.enrolment = enrolment ?? throw new ArgumentNullException(nameof(enrolment));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        replay = new ReplayMemory(settings.ReplayCapacity);
    }

    /// <summary>Verifies one signed envelope and counts the outcome. <paramref name="sessionSubject"/> is the session's subject when the message came through a gate session (rule 10).</summary>
    public VerifyResult Verify(ReadOnlyMemory<byte> signedEnvelope, string? sessionSubject = null)
    {
        var result = Check(signedEnvelope, sessionSubject);
        if (result.Refusal is { } reason) log.Refused(reason);
        else log.Accepted(result.Message!.Kind switch { ImKinds.Heartbeat => ImAccepted.Heartbeat, _ => throw new InvalidOperationException("a verified kind has no counter") });
        return result;
    }

    VerifyResult Check(ReadOnlyMemory<byte> signedEnvelope, string? sessionSubject)
    {
        try
        {
            var now = time.GetUtcNow();
            var nowMs = ImTime.ToWire(now);

            // Rule 1: size before anything is read.
            if (signedEnvelope.Length > settings.MaxFrameBytes) return VerifyResult.Refuse(ImRefusal.Oversize);

            // Rule 2: the envelope parses strictly.
            var envelope = ImEnvelopeCodec.Parse(signedEnvelope, settings.MaxPayloadBytes);

            // Rule 3: the token parses strictly (P-256 subject key, consistent key id).
            var token = ImTokenCodec.Parse(envelope.Token);

            // Rules 4 to 9: the token.
            if (CheckToken(token, now) is { } tokenRefusal) return VerifyResult.Refuse(tokenRefusal);

            // Rule 10: inside a session, the session's subject.
            if (sessionSubject is not null && !string.Equals(sessionSubject, token.Subject, StringComparison.Ordinal)) return VerifyResult.Refuse(ImRefusal.SessionMismatch);

            // Rule 11: the sender's signature, with the key the token names.
            if (!ImKeys.Verify(token.SubjectKey, ImTags.Input(ImTags.Message, envelope.Body), envelope.Signature)) return VerifyResult.Refuse(ImRefusal.BadMessageSignature);

            // Rule 12: fresh, both ways.
            var skew = (long)settings.ClockSkew.TotalMilliseconds;
            if (envelope.SentAt < nowMs - skew || envelope.SentAt > nowMs + skew) return VerifyResult.Refuse(ImRefusal.Stale);

            // Rule 13: a kind in the table.
            if (!ImKinds.Required.TryGetValue(envelope.Kind, out var permission)) return VerifyResult.Refuse(ImRefusal.UnknownKind);

            // Rule 14: the permission the kind needs.
            if (!token.HasPermission(permission)) return VerifyResult.Refuse(ImRefusal.WrongPermission);

            // Rule 15: the payload parses as its kind says.
            var heartbeat = envelope.Kind == ImKinds.Heartbeat ? ImHeartbeat.Parse(envelope.Payload) : null;

            // Rule 16: not seen before; remembered until the freshness rule would refuse it anyway.
            switch (replay.TryRemember(token.SubjectKeyId, envelope.MessageId, envelope.SentAt + skew, nowMs))
            {
                case ReplayOutcome.Seen: return VerifyResult.Refuse(ImRefusal.Replayed);
                case ReplayOutcome.Full: return VerifyResult.Refuse(ImRefusal.ReplayCacheFull);
            }

            return new VerifyResult(new VerifiedMessage(token.Subject, token.Role, token.SubjectKeyIdHex, Convert.ToHexStringLower(token.TokenId), envelope.Kind, envelope.MessageId,
                ImTime.FromWire(envelope.SentAt), now, heartbeat), null);
        }
        catch (ImFormatException e)
        {
            return VerifyResult.Refuse(e.Error == ImFormatError.UnknownVersion ? ImRefusal.UnknownVersion : ImRefusal.Malformed);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Fail closed. The exception is dropped, never logged: it may hold bytes of the frame.
            return VerifyResult.Refuse(ImRefusal.Malformed);
        }
    }

    /// <summary>Rules 4 to 9: the token on its own. The gate uses the same rules for the token in the handshake response.</summary>
    internal ImRefusal? CheckToken(ImToken token, DateTimeOffset now)
    {
        var nowMs = ImTime.ToWire(now);

        // Rule 4: the token signer chains to a trusted root in the role ImTokenSigner.
        using var signer = ImChains.Load(token.SignerChain);
        var verdict = trust.Validate(signer.EndEntity, signer.Intermediates, TrustRole.ImTokenSigner, now);
        if (!verdict.Ok)
            return verdict.Refusal is ChainRefusal.Expired or ChainRefusal.NotYetValid ? ImRefusal.CertificateExpired : ImRefusal.UntrustedTokenSigner;

        // Rule 5: Doppler's signature.
        if (!ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(signer.EndEntity), ImTags.Input(ImTags.Token, token.Body), token.Signature)) return ImRefusal.BadTokenSignature;

        // Rule 6: the token's own shape of time. Int128 so no pair of 64-bit times can overflow.
        if (token.ExpiresAt < token.NotBefore || (Int128)token.ExpiresAt - token.NotBefore > (Int128)settings.TokenMaxLifetime.TotalMilliseconds) return ImRefusal.TokenPolicy;

        // Rule 7: not before (with the skew), rule 8: not after (no skew on expiry).
        var skew = (long)settings.ClockSkew.TotalMilliseconds;
        if (nowMs + skew < token.NotBefore) return ImRefusal.TokenNotYetValid;
        if (nowMs >= token.ExpiresAt) return ImRefusal.TokenExpired;

        // Rule 9: Doppler's enrolment.
        var record = enrolment.Find(token.Subject);
        if (record is null) return ImRefusal.NotEnrolled;
        if (record.Revoked) return ImRefusal.Revoked;
        if (record.Role != token.Role || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(record.KeyId), token.SubjectKeyId)) return ImRefusal.EnrolmentMismatch;
        return null;
    }
}
