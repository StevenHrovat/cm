using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Im;
using Im.Trust;

namespace Cm.Gravity;

// The constructor names below are the model's (CM.BlackHole/gravity/C02/model.bend); the encoder writes them as Bend source.
enum Parse { POk, PMalformed, PUnknownVersion }

enum Chain { COk, CExpired, CUntrusted }

enum Enrol { EOk, ENotEnrolled, ERevoked, EMismatch }

enum Session { SNone, SSame, SOther }

enum Perm { SessionOpen, HeartbeatSend, OtherPerm }

/// <summary>
/// The facts the model's verifier decides on (model.bend, <c>Facts</c>), computed from a frame by the same world functions the C# uses
/// (the strict parsers, chain validation, signatures, Doppler's register), but each on its own: nothing here follows the verifier's
/// order or stops early. Times are wire milliseconds. <see cref="Key"/> and <see cref="Id"/> are the subject key id and the message id.
/// </summary>
/// <see cref="Outside"/> names the world function that threw where the C# answers Malformed but the model has no such answer at that rule
/// (rules 5, 9, 11); the fact is then written as failing, so the model refuses there with <see cref="OutsideReason"/>, and the C# must
/// answer Malformed exactly when the model gives that reason.
sealed record Facts(bool Oversize, Parse Env, Parse Tok, Chain Chain, bool TokSig, long Nbf, long Exp, Enrol Enrol, Session Session, bool MsgSig,
    long Sent, bool Heartbeat, IReadOnlyList<Perm> Perms, Parse Payload, string Key, string Id, string? Outside = null, string? OutsideReason = null)
{
    /// <summary>False when rule 1, 2 or 3 refuses, so no time, key or id of the frame is read.</summary>
    public bool ReachesToken => !Oversize && Env == Parse.POk && Tok == Parse.POk;
}

static class FactProbe
{
    /// <summary>
    /// The facts of one frame at <paramref name="now"/>. Assumption written down in gravity.md: a token whose signer chain does not load
    /// counts as a token that does not parse (rule 3), which gives the same answer at the same place as the C#'s Malformed in rule 4.
    /// </summary>
    public static Facts Probe(ReadOnlyMemory<byte> frame, string? sessionSubject, DateTimeOffset now, MilkyWaySettingsView settings, ITrustAnchors trust, IEnrolmentRegistry enrolment)
    {
        if (ImKinds.Required.Count != 1 || !ImKinds.Required.TryGetValue(ImKinds.Heartbeat, out var heartbeatPermission) || heartbeatPermission != ImKinds.HeartbeatPermission)
            throw new InvalidOperationException("the table of kinds changed: the model (MsgKind, need) and this probe must change with it");
        var none = new Facts(frame.Length > settings.MaxFrameBytes, Parse.POk, Parse.POk, Chain.COk, true, 0, 0, Enrol.EOk, Session.SNone, true, 0, true, [Perm.HeartbeatSend], Parse.POk, "", "");

        if (Try(() => ImEnvelopeCodec.Parse(frame, settings.MaxPayloadBytes), out var envelope) is { } env) return none with { Env = env };
        if (Try(() => ImTokenCodec.Parse(envelope!.Token), out var token) is { } tok) return none with { Tok = tok };
        LoadedChain signer;
        try { signer = ImChains.Load(token!.SignerChain); }
        catch (Exception e) when (e is not OutOfMemoryException) { return none with { Tok = Parse.PMalformed }; }
        using (signer)
        {
            ChainVerdict verdict;
            try { verdict = trust.Validate(signer.EndEntity, signer.Intermediates, TrustRole.ImTokenSigner, now); }
            catch (Exception e) when (e is not OutOfMemoryException) { return none with { Tok = Parse.PMalformed }; }
            var chain = verdict.Ok ? Chain.COk : verdict.Refusal is ChainRefusal.Expired or ChainRefusal.NotYetValid ? Chain.CExpired : Chain.CUntrusted;
            string? outside = null, outsideReason = null;
            bool Outside(string where, string reason, Func<bool> check)
            {
                try { return check(); }
                catch (Exception e) when (e is not OutOfMemoryException) { outside ??= where; outsideReason ??= reason; return false; }
            }
            var tokSig = Outside("token signature", "BadTokenSignature", () => ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(signer.EndEntity), ImTags.Input(ImTags.Token, token.Body), token.Signature));
            var record = enrolment.Find(token.Subject);
            var enrol = record is null ? Enrol.ENotEnrolled
                : record.Revoked ? Enrol.ERevoked
                : record.Role != token.Role || !Outside("enrolled key id", "EnrolmentMismatch", () => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(record.KeyId), token.SubjectKeyId)) ? Enrol.EMismatch
                : Enrol.EOk;
            var session = sessionSubject is null ? Session.SNone : string.Equals(sessionSubject, token.Subject, StringComparison.Ordinal) ? Session.SSame : Session.SOther;
            var msgSig = Outside("message signature", "BadMessageSignature", () => ImKeys.Verify(token.SubjectKey, ImTags.Input(ImTags.Message, envelope!.Body), envelope.Signature));
            var heartbeat = envelope!.Kind == ImKinds.Heartbeat;
            var perms = token.Permissions.Select(p => p switch { ImKinds.SessionPermission => Perm.SessionOpen, ImKinds.HeartbeatPermission => Perm.HeartbeatSend, _ => Perm.OtherPerm }).ToList();
            var payload = heartbeat ? Try(() => ImHeartbeat.Parse(envelope.Payload), out _) ?? Parse.POk : Parse.POk;
            return new Facts(none.Oversize, Parse.POk, Parse.POk, chain, tokSig, token.NotBefore, token.ExpiresAt, enrol, session, msgSig,
                envelope.SentAt, heartbeat, perms, payload, Convert.ToHexString(token.SubjectKeyId), Convert.ToHexString(envelope.MessageId), outside, outsideReason);
        }
    }

    /// <summary>Null when <paramref name="parse"/> succeeds, else what a strict parser's failure means for the model.</summary>
    static Parse? Try<T>(Func<T> parse, out T? value)
    {
        try { value = parse(); return null; }
        catch (ImFormatException e) { value = default; return e.Error == ImFormatError.UnknownVersion ? Parse.PUnknownVersion : Parse.PMalformed; }
        catch (Exception e) when (e is not OutOfMemoryException) { value = default; return Parse.PMalformed; }
    }
}

/// <summary>The settings the model reads (<c>Cfg</c>) plus the two frame limits the probe needs.</summary>
sealed record MilkyWaySettingsView(int MaxFrameBytes, int MaxPayloadBytes, long SkewMs, long MaxLifeMs, int Capacity);

/// <summary>
/// Writes facts as Bend source. Bend's JavaScript backend holds a Nat up to 2^48 - 1 and its literals go up to 2^32 - 1, so a time is
/// written relative to <see cref="Base"/> (the world's start less 2^31 ms), as <c>hi * 65536 * 65536 + lo</c> when it needs more than 32 bits; a
/// time outside 0 to 2^48 - 1 from the base cannot be written and the case is counted as out of range, never silently changed.
/// </summary>
sealed class BendText(long baseMs)
{
    public const long Span = 1L << 48;
    readonly Dictionary<string, int> keys = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);

    public long Base => baseMs;

    public bool Fits(long t) => t - baseMs is >= 0 and < Span;

    public static string Nat(long n)
    {
        if (n < 0 || n >= Span) throw new ArgumentOutOfRangeException(nameof(n), "outside the JavaScript backend's Nat");
        return n < 1L << 32 ? n.ToString(CultureInfo.InvariantCulture) + "n"
            : string.Create(CultureInfo.InvariantCulture, $"Nat.add(Nat.mul(Nat.mul({n >> 32}n, 65536n), 65536n), {n & 0xFFFFFFFFL}n)");
    }

    public string Time(long t) => Nat(t - baseMs);

    static string B(bool b) => b ? "True{}" : "False{}";

    static string C<T>(T e) where T : struct, Enum => "M." + e + "{}";

    /// <summary>Equal keys get equal numbers and different keys different ones, which is all the model asks of them.</summary>
    static int Intern(Dictionary<string, int> table, string value)
    {
        if (!table.TryGetValue(value, out var n)) table[value] = n = table.Count;
        return n;
    }

    /// <summary>Whether every time the model may read of these facts can be written.</summary>
    public bool Fits(Facts f, long now) => Fits(now) && (!f.ReachesToken || (Fits(f.Nbf) && Fits(f.Exp) && Fits(f.Sent)));

    public string Facts(Facts f)
    {
        // a frame that rules 1 to 3 refuse has no times to read; 0n stands in
        string T(long t) => f.ReachesToken ? Time(t) : "0n";
        var s = new StringBuilder("M.Facts{");
        s.AppendJoin(", ", B(f.Oversize), C(f.Env), C(f.Tok), C(f.Chain), B(f.TokSig), T(f.Nbf), T(f.Exp), C(f.Enrol), C(f.Session), B(f.MsgSig),
            T(f.Sent), f.Heartbeat ? "M.Heartbeat{}" : "M.Unlisted{}", "[" + string.Join(", ", f.Perms.Select(C)) + "]", C(f.Payload),
            Nat(Intern(keys, f.Key)), Nat(Intern(ids, f.Id)));
        return s.Append('}').ToString();
    }
}
