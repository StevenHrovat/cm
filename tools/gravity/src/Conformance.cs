using System.Globalization;
using System.Text;
using Im;
using Im.Trust;
using CM.MilkyWay;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;

namespace Cm.Gravity;

/// <summary>One input to the verifier: the frame, the session's subject if any, and how far the clock moves before it arrives.</summary>
sealed record ConformCase(byte[] Frame, string? SessionSubject, TimeSpan Advance);

/// <summary>
/// <c>gravity conform</c> (gravity.md, slice G1, "Done when"): the C2 model compiled to JavaScript and <see cref="ImVerifier"/> must give
/// the same answer, verified or the same refusal reason, for every case of a deterministic campaign in the sandbox world, run as one
/// sequence through one verifier so the replay memory is compared too. The facts come from <see cref="FactProbe"/>; what this compares is
/// the verifier's own logic: the order of the rules, the time arithmetic, the permission table and the replay memory.
/// </summary>
static class Conform
{
    /// <summary>The model's refusal reasons in the model's order; the model writes 0 for verified and 1 + this index for a refusal.</summary>
    public static readonly string[] Reasons =
    [
        "Oversize", "Malformed", "UnknownVersion", "UntrustedTokenSigner", "CertificateExpired", "BadTokenSignature", "TokenPolicy", "TokenNotYetValid",
        "TokenExpired", "NotEnrolled", "Revoked", "EnrolmentMismatch", "SessionMismatch", "BadMessageSignature", "Stale", "UnknownKind", "WrongPermission",
        "Replayed", "ReplayCacheFull",
    ];

    const int Chunk = 250; // cases per Bend def: Node's parser runs out of stack on one literal of thousands

    public static bool Run(Bend bend, string section, int count, int seed, TextWriter output)
    {
        var start = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        // a small memory, so a campaign reaches ReplayCacheFull; skew and lifetime are the defaults
        using var world = new SandboxWorld(start, new MilkyWaySettings { ReplayCapacity = 6 });
        var settings = new MilkyWaySettingsView(world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, (long)world.Settings.ClockSkew.TotalMilliseconds,
            (long)world.Settings.TokenMaxLifetime.TotalMilliseconds, world.Settings.ReplayCapacity);
        var text = new BendText(ImTime.ToWire(start) - (1L << 31));
        var generator = new CaseGenerator(world, seed);

        var cases = new List<(Facts Facts, long Now, string CSharp)>();
        var outOfRange = 0;
        for (var i = 0; i < count; i++)
        {
            var c = generator.Next();
            world.Clock.Advance(c.Advance);
            var now = world.Clock.GetUtcNow();
            var facts = FactProbe.Probe(c.Frame, c.SessionSubject, now, settings, world.Anchors, world.Enrolment);
            // a case the model cannot hold is not sent to the C# either, so both see the same sequence
            if (!text.Fits(facts, ImTime.ToWire(now))) { outOfRange++; continue; }
            var result = world.Gate.Verifier.Verify(c.Frame, c.SessionSubject);
            cases.Add((facts, ImTime.ToWire(now), result.IsVerified ? "Verified" : result.Refusal!.Value.ToString()));
        }

        var dir = Directory.CreateTempSubdirectory("cm-gravity-conform-").FullName;
        try
        {
            File.Copy(Path.Combine(section, "model.bend"), Path.Combine(dir, "model.bend"));
            File.WriteAllText(Path.Combine(dir, "conform.bend"), Program(text, settings, cases.Select(c => (c.Facts, c.Now)).ToList()));
            var compiled = bend.Run(dir, "conform.bend", "-o", "conform.js");
            if (compiled.Exit != 0) { output.WriteLine("conform: the generated file does not compile:\n" + compiled.Output); return false; }
            var ran = bend.Node(dir, "conform.js");
            var codes = ran.Exit == 0 ? ParseCodes(ran.Output) : null;
            if (codes is null || codes.Count != cases.Count + 1) { output.WriteLine("conform: the model did not answer every case:\n" + ran.Output[..Math.Min(ran.Output.Length, 600)]); return false; }

            var mismatches = 0;
            var outside = 0;
            var seen = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < cases.Count; i++)
            {
                var model = codes[i + 1] == 0 ? "Verified" : Reasons[codes[i + 1] - 1];
                var (facts, now, csharp) = cases[i];
                var expected = facts.Outside is not null && model == facts.OutsideReason ? "Malformed" : model;
                if (facts.Outside is not null) outside++;
                seen[csharp] = seen.GetValueOrDefault(csharp) + 1;
                if (expected == csharp) continue;
                if (mismatches++ < 10) output.WriteLine($"MISMATCH case {i}: model {model}, ImVerifier {csharp}, at {text.Time(now)}: {text.Facts(facts)}");
            }
            output.WriteLine($"conform: {cases.Count} cases (seed {seed}), {mismatches} mismatches, {outOfRange} out of the JavaScript Nat range, {outside} outside the model (C# threw)");
            output.WriteLine("    answers: " + string.Join(", ", seen.Select(kv => kv.Key + " " + kv.Value)));
            var missing = Reasons.Append("Verified").Where(r => !seen.ContainsKey(r)).ToList();
            if (missing.Count > 0) output.WriteLine("    never reached: " + string.Join(", ", missing));
            return mismatches == 0;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The Bend program that runs the cases through the model's verifier in order, one replay memory throughout.</summary>
    internal static string Program(BendText text, MilkyWaySettingsView settings, IReadOnlyList<(Facts Facts, long Now)> cases)
    {
        var s = new StringBuilder();
        s.AppendLine("# Generated by tools/gravity conform; never committed.");
        s.AppendLine("import Base").AppendLine("import ./model.bend as M").AppendLine();
        s.AppendLine("type Case is Data:").AppendLine("  Case{f: M.Facts, now: Nat}").AppendLine();
        s.AppendLine("def reason(w: M.Refusal) -> Nat:").AppendLine("  match w:");
        for (var i = 0; i < Reasons.Length; i++) s.AppendLine($"    case M.{Reasons[i]}{{}}:").AppendLine($"      {i + 1}n");
        s.AppendLine();
        s.AppendLine("def code(o: M.Out) -> Nat:").AppendLine("  match o:").AppendLine("    case M.Verified{m}:").AppendLine("      0n");
        s.AppendLine("    case M.Refused{w, m}:").AppendLine("      reason(w)").AppendLine();
        s.AppendLine("# the first code is the start's, not a case's");
        s.AppendLine("def run(cs: List<&2, Case>, +cfg: M.Cfg, +prev: M.Out) -> List<&2, Nat>:");
        s.AppendLine("  match cs:").AppendLine("    case Nil{}:").AppendLine("      [code(prev)]").AppendLine("    case c <> rest:").AppendLine("      match c:");
        s.AppendLine("        case Case{f, now}:").AppendLine("          code(prev) <> run(rest, cfg, M.verify(f, now, cfg, M.memory(prev)))").AppendLine();
        var chunks = cases.Chunk(Chunk).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            s.AppendLine($"def b{i}() -> List<&2, Case>:");
            s.Append("  [").AppendJoin(",\n   ", chunks[i].Select(c => $"Case{{{text.Facts(c.Facts)}, {text.Time(c.Now)}}}")).AppendLine("]").AppendLine();
        }
        var all = "Nil{}";
        for (var i = chunks.Count - 1; i >= 0; i--) all = $"List.append(&2, Case, b{i}(), {all})";
        s.AppendLine("def main() -> List<&2, Nat>:");
        s.AppendLine($"  run({all}, M.Cfg{{{BendText.Nat(settings.MaxLifeMs)}, {BendText.Nat(settings.SkewMs)}, {BendText.Nat(settings.Capacity)}}}, M.Verified{{[]}})");
        return s.ToString();
    }

    /// <summary>The list the compiled main prints, like <c>[0n, 3n]</c>.</summary>
    internal static List<int>? ParseCodes(string output)
    {
        var line = output.Split('\n').LastOrDefault(l => l.StartsWith('['))?.Trim();
        if (line is null || !line.EndsWith(']')) return null;
        var codes = new List<int>();
        foreach (var part in line[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.EndsWith('n') || !int.TryParse(part[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > Reasons.Length) return null;
            codes.Add(n);
        }
        return codes;
    }
}

/// <summary>
/// Deterministic cases for the verifier in the sandbox world: every way chapter 04 refuses, built the way a device, a forger or a
/// broken clock would build it, plus resent frames (replay) and byte flips (parsers). The same seed gives the same campaign.
/// </summary>
sealed class CaseGenerator
{
    readonly SandboxWorld world;
    readonly Random random;
    readonly List<SimulatedDevice> enrolled = [];
    readonly SimulatedDevice revoked, unenrolled, wrongRole, wrongKey;
    readonly IImSigner foreignSigner, expiredSigner;
    readonly List<byte[]> sent = [];

    public CaseGenerator(SandboxWorld world, int seed)
    {
        this.world = world;
        random = new Random(seed);
        var start = world.Clock.GetUtcNow();
        for (var i = 0; i < 4; i++) enrolled.Add(world.EnrolDevice());
        revoked = world.EnrolDevice();
        world.Enrolment.Revoke(revoked.Subject);
        unenrolled = world.UnenrolledDevice();
        wrongRole = world.EnrolDevice(EnrolledRole.Laptop);
        world.Enrolment.Put(new EnrolmentRecord(wrongRole.Subject, EnrolledRole.Runner, wrongRole.Key.KeyId, false));
        wrongKey = world.EnrolDevice();
        world.Enrolment.Put(new EnrolmentRecord(wrongKey.Subject, wrongKey.Role, enrolled[0].Key.KeyId, false));
        foreignSigner = world.Foreign.Issue(TrustRole.ImTokenSigner, "foreign-token-signer", start.AddDays(-1), start.AddDays(365));
        expiredSigner = world.Authority.Issue(TrustRole.ImTokenSigner, "old-token-signer", start.AddDays(-20), start.AddDays(-10));
    }

    T Pick<T>(params (int Weight, T Value)[] choices)
    {
        var roll = random.Next(choices.Sum(c => c.Weight));
        foreach (var (weight, value) in choices)
            if ((roll -= weight) < 0) return value;
        throw new InvalidOperationException();
    }

    public ConformCase Next()
    {
        var skew = world.Settings.ClockSkew;
        var advance = Pick((60, TimeSpan.FromMilliseconds(random.Next(0, 2000))), (8, skew + TimeSpan.FromMilliseconds(random.Next(0, 5000))), (2, TimeSpan.FromHours(random.Next(1, 30))));
        // the frame is built at the time it arrives
        var now = world.Clock.GetUtcNow() + advance;

        if (sent.Count > 0 && random.Next(100) < 15)
            return new ConformCase(sent[sent.Count - 1 - random.Next(Math.Min(sent.Count, 20))], null, advance);

        var device = Pick((80, enrolled[random.Next(enrolled.Count)]), (5, revoked), (5, unenrolled), (5, wrongRole), (5, wrongKey));
        var token = Token(device, now);
        if (random.Next(100) < 4) token[token.Length - 1 - random.Next(8)] ^= (byte)(1 << random.Next(8)); // in the token signature
        var kind = Pick((92, ImKinds.Heartbeat), (8, "im.unknown"));
        var payload = Pick((88, new ImHeartbeat((ulong)random.Next(), ImHeartbeatStatus.Healthy, "0.1.0").Encode()), (6, [0xFF, 0x00, 0x01]), (6, new ImHeartbeat(1, ImHeartbeatStatus.Degraded, "0.1.0").Encode()[..^1]));
        var key = random.Next(100) < 6 ? enrolled[(enrolled.IndexOf(device) + 1 + enrolled.Count) % enrolled.Count].Key : device.Key;
        var sentAt = now + Pick((80, TimeSpan.Zero), (4, skew), (4, -skew), (4, skew + TimeSpan.FromMilliseconds(1)), (4, -skew - TimeSpan.FromMilliseconds(1)), (4, TimeSpan.FromDays(random.Next(-3, 4))));
        byte[] frame;
        try { frame = ImEnvelopeCodec.Seal(kind, payload, token, key, sentAt, world.Settings.MaxPayloadBytes); }
        catch (ArgumentException) { frame = ImEnvelopeCodec.Seal(kind, payload, device.Token, key, now, world.Settings.MaxPayloadBytes); }
        if (random.Next(100) < 8) frame[random.Next(frame.Length)] ^= (byte)(1 << random.Next(8));
        if (random.Next(100) < 2) frame = [.. frame, .. new byte[world.Settings.MaxFrameBytes + 1 - frame.Length]];
        var session = Pick((80, (string?)null), (12, device.Subject), (8, "d-another-subject"));
        sent.Add(frame);
        return new ConformCase(frame, session, advance);
    }

    byte[] Token(SimulatedDevice device, DateTimeOffset now)
    {
        var life = world.Settings.TokenMaxLifetime;
        var skew = world.Settings.ClockSkew;
        IReadOnlyCollection<string> permissions = Pick<IReadOnlyCollection<string>>(
            (70, SandboxWorld.DefaultPermissions), (10, [ImKinds.HeartbeatPermission]), (8, [ImKinds.SessionPermission]), (6, ["z.other"]), (6, [ImKinds.SessionPermission, "z.other"]));
        var notBefore = now + Pick((80, TimeSpan.FromMinutes(-1)), (5, skew), (5, skew + TimeSpan.FromMilliseconds(1)), (5, TimeSpan.FromHours(-30)), (5, TimeSpan.FromMinutes(-1) - life));
        var expiresAt = notBefore + Pick((80, TimeSpan.FromHours(8)), (6, life), (6, life + TimeSpan.FromMilliseconds(1)), (8, now - notBefore + TimeSpan.FromMilliseconds(random.Next(-2, 3))));
        var signer = Pick((90, world.TokenSigner), (5, foreignSigner), (5, expiredSigner));
        try
        {
            var claims = new ImTokenClaims(device.Subject, device.Role, ImKeys.SubjectPublicKeyInfo(device.Key.Certificate), permissions, notBefore, expiresAt);
            return ImTokenCodec.Issue(claims, signer, now);
        }
        catch (ArgumentException) { return device.Token.ToArray(); }
    }
}
