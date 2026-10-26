using System.Security.Cryptography;
using Im;
using CM.MilkyWay.Verify;

namespace CM.MilkyWay.Simulation;

/// <summary>What the fuzzer feeds (DMP chapter 06, Supernova level 3: "fuzzing of every parser that takes bytes from another machine").</summary>
public enum FuzzTarget
{
    /// <summary><see cref="ImTokenCodec.Parse"/>: only <see cref="ImFormatException"/> may escape, and what parses re-encodes to the same bytes.</summary>
    Token,
    /// <summary><see cref="ImEnvelopeCodec.Parse"/>, same oracle.</summary>
    Envelope,
    /// <summary><see cref="ImHeartbeat.Parse"/>, same oracle.</summary>
    Heartbeat,
    /// <summary>Every frame parser by its type byte (knock, accept, response, established, ack) and the certificate chains inside.</summary>
    Frames,
    /// <summary>The verifier on a changed signed envelope: it never throws and never verifies anything but the bytes the device signed.</summary>
    Verifier,
    /// <summary>A gate session on a changed handshake response: it never throws and never establishes on anything but the device's own response.</summary>
    Handshake,
}

/// <summary>An input that broke an oracle. The input is synthetic (sandbox keys, sandbox subjects); it is kept to replay, never logged as text.</summary>
public sealed record FuzzFinding(FuzzTarget Target, string Oracle, byte[] Input);

/// <summary>The result of a campaign: how many inputs ran, how many distinct outcomes they reached, the findings.</summary>
public sealed record FuzzReport(FuzzTarget Target, int Iterations, IReadOnlyDictionary<string, int> Outcomes, IReadOnlyList<FuzzFinding> Findings);

/// <summary>
/// A deterministic mutational fuzzer for IM version 0, run in the sandbox world. The same seed gives the same mutations, so a finding
/// replays. It is not coverage-guided: inputs are kept when they reach an outcome the campaign had not seen, and the corpus in the
/// repository (seeds plus every input that once broke an oracle) is replayed by <c>tools/verify</c>. Nothing here touches a network.
/// </summary>
public sealed class ImFuzzer
{
    static readonly byte[] Interesting = [0x00, 0x01, 0x02, 0x03, 0x04, 0x0F, 0x10, 0x11, 0x1F, 0x20, 0x40, 0x7E, 0x7F, 0x80, 0x81, 0xFE, 0xFF];

    readonly SandboxWorld world;
    readonly Random random;
    readonly List<byte[]> pool = [];

    public ImFuzzer(SandboxWorld world, int seed)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        random = new Random(seed);
    }

    /// <summary>Valid inputs for a parser target, made fresh in this world: the starting points of a campaign and of the corpus.</summary>
    public IReadOnlyList<byte[]> Seeds(FuzzTarget target)
    {
        var device = world.EnrolDevice();
        var now = world.Clock.GetUtcNow();
        var heartbeat = new ImHeartbeat(42, ImHeartbeatStatus.Degraded, "0.1.0-sandbox");
        var envelope = ImEnvelopeCodec.Seal(ImKinds.Heartbeat, heartbeat.Encode(), device.Token, device.Key, now, world.Settings.MaxPayloadBytes);
        var client = device.NewClient();
        var knock = client.Knock(world.KnockSecret);
        var session = world.Gate.Open();
        var accept = session.Receive(knock).Frame!;
        var response = client.Respond(accept);
        return target switch
        {
            FuzzTarget.Token => [device.Token, world.IssueToken(device, permissions: ["im.heartbeat.send", "im.session.open", "z.last-permission"])],
            FuzzTarget.Envelope or FuzzTarget.Verifier => [envelope],
            FuzzTarget.Heartbeat => [heartbeat.Encode(), new ImHeartbeat(ulong.MaxValue, ImHeartbeatStatus.Failing, new string('~', ImLimits.AgentVersionMax)).Encode()],
            FuzzTarget.Frames or FuzzTarget.Handshake => [knock, accept, response, ImFrames.Established(new byte[16]), ImFrames.Ack(new byte[16]), ImFrames.Refused()],
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    /// <summary>
    /// Runs <paramref name="iterations"/> mutated inputs against <paramref name="target"/>. <paramref name="corpus"/> adds inputs to start
    /// from; <paramref name="keep"/> hears each input that reached a new outcome (the long run saves them to the corpus).
    /// </summary>
    public FuzzReport Campaign(FuzzTarget target, int iterations, IEnumerable<byte[]>? corpus = null, Action<string, byte[]>? keep = null)
    {
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var findings = new List<FuzzFinding>();
        pool.Clear();
        pool.AddRange(Seeds(target is FuzzTarget.Verifier or FuzzTarget.Handshake ? FuzzTarget.Frames : target));
        var starts = new List<byte[]>(target is FuzzTarget.Verifier or FuzzTarget.Handshake ? [] : Seeds(target));
        if (corpus is not null) starts.AddRange(corpus);
        if (target is FuzzTarget.Verifier) starts.AddRange(Seeds(FuzzTarget.Verifier));

        for (var i = 0; i < iterations; i++)
        {
            string outcome;
            byte[] input;
            switch (target)
            {
                case FuzzTarget.Handshake:
                    (outcome, input) = HandshakeOnce(findings);
                    break;
                default:
                    var original = starts[random.Next(starts.Count)];
                    input = Mutate(original);
                    outcome = Run(target, input, original, findings);
                    break;
            }
            if (!outcomes.TryGetValue(outcome, out var n)) keep?.Invoke(outcome, input);
            outcomes[outcome] = n + 1;
            if (pool.Count < 256 && random.Next(16) == 0) pool.Add(input);
        }
        return new FuzzReport(target, iterations, outcomes, findings);
    }

    /// <summary>Runs one input against a parser target or the verifier and applies the oracle. Returns the outcome; a broken oracle is added to <paramref name="findings"/>.</summary>
    public string Run(FuzzTarget target, byte[] input, byte[]? original, List<FuzzFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(findings);
        try
        {
            switch (target)
            {
                case FuzzTarget.Token:
                    var token = ImTokenCodec.Parse(input);
                    if (!ImCanonical.Token(token).AsSpan().SequenceEqual(input)) findings.Add(new(target, "a parsed token re-encodes to other bytes (I6)", input));
                    return "parsed";
                case FuzzTarget.Envelope:
                    var envelope = ImEnvelopeCodec.Parse(input, world.Settings.MaxPayloadBytes);
                    if (!ImCanonical.Envelope(envelope).AsSpan().SequenceEqual(input)) findings.Add(new(target, "a parsed envelope re-encodes to other bytes (I6)", input));
                    return "parsed";
                case FuzzTarget.Heartbeat:
                    var heartbeat = ImHeartbeat.Parse(input);
                    if (!heartbeat.Encode().AsSpan().SequenceEqual(input)) findings.Add(new(target, "a parsed heartbeat re-encodes to other bytes (I6)", input));
                    return "parsed";
                case FuzzTarget.Frames:
                    return "parsed " + ParseFrame(input);
                case FuzzTarget.Verifier:
                    var result = world.Gate.Verifier.Verify(input);
                    if (result.IsVerified && (original is null || !input.AsSpan().SequenceEqual(original)))
                        findings.Add(new(target, "a changed envelope verified (I1)", input));
                    return result.IsVerified ? "verified" : "refused " + result.Refusal;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }
        catch (ImFormatException e) when (target is not FuzzTarget.Verifier)
        {
            return e.Error.ToString();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            findings.Add(new(target, "an exception other than ImFormatException escaped: " + e.GetType().Name, input));
            return "exception " + e.GetType().Name;
        }
    }

    (string Outcome, byte[] Input) HandshakeOnce(List<FuzzFinding> findings)
    {
        var device = world.EnrolDevice();
        var client = device.NewClient();
        var session = world.Gate.Open();
        var accept = session.Receive(client.Knock(world.KnockSecret)).Frame!;
        var response = client.Respond(accept);
        var input = Mutate(response);
        try
        {
            var reply = session.Receive(input);
            if (session.IsEstablished && !input.AsSpan().SequenceEqual(response)) findings.Add(new(FuzzTarget.Handshake, "a changed response established a session", input));
            return (session.IsEstablished ? "established" : reply.IsQuiet ? "quiet" : "refused", input);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            findings.Add(new(FuzzTarget.Handshake, "the gate session threw: " + e.GetType().Name, input));
            return ("exception " + e.GetType().Name, input);
        }
    }

    static string ParseFrame(byte[] input)
    {
        switch (ImFrames.TypeOf(input))
        {
            case ImFrameType.Knock:
                ImFrames.ParseKnock(input);
                return "knock";
            case ImFrameType.Accept:
                using (ImChains.Load(ImFrames.ParseAccept(input).GateChain)) return "accept";
            case ImFrameType.Response:
                var response = ImFrames.ParseResponse(input);
                ImTokenCodec.Parse(response.Token);
                using (ImChains.Load(response.DeviceChain)) return "response";
            case ImFrameType.Established:
                ImFrames.ParseEstablished(input);
                return "established";
            case ImFrameType.Ack:
                ImFrames.ParseAck(input);
                return "ack";
            case ImFrameType.Message:
                ImEnvelopeCodec.Parse(ImFrames.MessageBody(input), int.MaxValue);
                return "message";
            default:
                return "unknown type";
        }
    }

    /// <summary>One to four mutations of the kinds that break parsers: bit flips, interesting bytes, length fields, inserts, deletes, splices.</summary>
    public byte[] Mutate(byte[] original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var data = new List<byte>(original);
        var rounds = 1 + random.Next(4);
        for (var r = 0; r < rounds; r++)
        {
            var at = data.Count == 0 ? 0 : random.Next(data.Count);
            switch (random.Next(10))
            {
                case 0 when data.Count > 0:
                    data[at] ^= (byte)(1 << random.Next(8));
                    break;
                case 1 when data.Count > 0:
                    data[at] = Interesting[random.Next(Interesting.Length)];
                    break;
                case 2 when data.Count >= 2:
                    WriteLength(data, Math.Min(at, data.Count - 2), 2);
                    break;
                case 3 when data.Count >= 4:
                    WriteLength(data, Math.Min(at, data.Count - 4), 4);
                    break;
                case 4:
                    data.Insert(at, (byte)random.Next(256));
                    break;
                case 5 when data.Count > 1:
                    data.RemoveRange(at, Math.Min(1 + random.Next(8), data.Count - at));
                    break;
                case 6 when data.Count > 1:
                    data.RemoveRange(Math.Max(1, at), data.Count - Math.Max(1, at)); // truncate
                    break;
                case 7:
                    data.AddRange(RandomNumberGeneratorBytes(1 + random.Next(16)));
                    break;
                case 8 when pool.Count > 0 && data.Count > 0:
                    var donor = pool[random.Next(pool.Count)];
                    if (donor.Length == 0) break;
                    var from = random.Next(donor.Length);
                    var take = Math.Min(1 + random.Next(64), donor.Length - from);
                    data.RemoveRange(at, Math.Min(take, data.Count - at));
                    data.InsertRange(at, donor.Skip(from).Take(take));
                    break;
                case 9 when data.Count > 1:
                    var length = Math.Min(1 + random.Next(32), data.Count - at);
                    data.InsertRange(at, data.GetRange(at, length)); // duplicate a range
                    break;
                default:
                    if (data.Count > 0) data[at] = (byte)random.Next(256);
                    break;
            }
        }
        var result = data.ToArray();
        return result.AsSpan().SequenceEqual(original) && result.Length > 0 ? Flip(result) : result;
    }

    byte[] Flip(byte[] data)
    {
        data[random.Next(data.Length)] ^= 0x80;
        return data;
    }

    /// <summary>Big-endian length fields set to edges: zero, one, the rest of the input, just over it, the type's maximum.</summary>
    void WriteLength(List<byte> data, int at, int width)
    {
        var rest = (uint)(data.Count - at - width);
        uint[] edges = [0, 1, rest, rest + 1, rest - 1, width == 2 ? ushort.MaxValue : uint.MaxValue, width == 2 ? 0x8000u : 0x80000000u];
        var value = edges[random.Next(edges.Length)];
        for (var i = 0; i < width; i++) data[at + i] = (byte)(value >> (8 * (width - 1 - i)));
    }

    byte[] RandomNumberGeneratorBytes(int count)
    {
        var bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }
}

/// <summary>
/// Encodes parsed IM structures again, field by field, for the canonical-form oracle (invariant I6: what parses re-encodes to the same
/// bytes). Written from DMP chapter 04's tables, not from the codecs' writers, so a codec that accepts a second spelling is caught.
/// </summary>
public static class ImCanonical
{
    public static byte[] Token(ImToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var reader = new ImReader(token.Body);
        reader.Magic("IMT", 0);
        reader.Fixed(16);
        reader.Id8(ImAlphabet.Subject, ImLimits.SubjectMax);
        reader.Id8(ImAlphabet.Name, ImLimits.RoleMax);
        reader.Fixed(32);
        reader.Bytes16(ImLimits.KeyMax);
        var count = reader.U8();
        for (var i = 0; i < count; i++) reader.Id8(ImAlphabet.Name, ImLimits.NameMax);
        reader.I64();
        reader.I64();
        reader.I64();
        var nonce = reader.Fixed(16);

        var writer = new ImWriter()
            .Magic("IMT", 0)
            .Fixed(token.TokenId, 16)
            .Id8(token.Subject, ImAlphabet.Subject, ImLimits.SubjectMax)
            .Id8(ImRoles.ToWire(token.Role), ImAlphabet.Name, ImLimits.RoleMax)
            .Fixed(token.SubjectKeyId, 32)
            .Bytes16(token.SubjectKey, ImLimits.KeyMax)
            .U8((byte)token.Permissions.Count);
        foreach (var permission in token.Permissions) writer.Id8(permission, ImAlphabet.Name, ImLimits.NameMax);
        var body = writer.I64(token.IssuedAt).I64(token.NotBefore).I64(token.ExpiresAt).Fixed(nonce, 16)
            .List16(token.SignerChain, ImLimits.ChainMax, ImLimits.CertificateMax).ToArray();
        return new ImWriter().Bytes32(body, ImLimits.TokenMax).Fixed(token.Signature, ImLimits.SignatureLength).ToArray();
    }

    public static byte[] Envelope(ImEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var body = new ImWriter()
            .Magic("IMM", 0)
            .Id8(envelope.Kind, ImAlphabet.Name, ImLimits.NameMax)
            .Fixed(envelope.MessageId, 16)
            .I64(envelope.SentAt)
            .Bytes32(envelope.Token, ImLimits.TokenMax + 4 + ImLimits.SignatureLength)
            .Bytes32(envelope.Payload, int.MaxValue)
            .ToArray();
        return new ImWriter().Bytes32(body, int.MaxValue).Fixed(envelope.Signature, ImLimits.SignatureLength).ToArray();
    }
}
