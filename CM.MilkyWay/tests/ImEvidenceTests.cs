using System.Reflection;
using System.Text;
using Im;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests;

/// <summary>
/// The invariants of DMP chapter 04 ("Invariants for proving") as tests: I1 by mutation (no single changed byte of a valid message
/// verifies), I3 by the shape of the log interface, and "no secret or content in any log" by searching everything the gate emits.
/// </summary>
[Trait("Category", "im")]
public sealed class ImEvidenceTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxWorld world = new(Start);

    public void Dispose() => world.Dispose();

    [Fact]
    public void I1_no_single_flipped_byte_of_a_valid_message_verifies()
    {
        var device = world.EnrolDevice();
        var (connection, client) = device.Handshake();
        Assert.True(connection.Session.IsEstablished);
        var envelope = ImFrames.MessageBody(client.Heartbeat(device.NextHeartbeat())).ToArray();

        // Every byte position, each with one bit flipped, outside any session so every rule is exercised on its own.
        for (var i = 0; i < envelope.Length; i++)
        {
            var mutated = (byte[])envelope.Clone();
            mutated[i] ^= (byte)(1 << (i % 8));
            Assert.False(world.Gate.Verifier.Verify(mutated).IsVerified, $"a message with byte {i} changed verified");
        }

        Assert.True(world.Gate.Verifier.Verify(envelope).IsVerified);
        Assert.Equal(envelope.Length, world.Counters.TotalRefused);
    }

    [Fact]
    public void I1_truncated_and_extended_messages_never_verify()
    {
        var device = world.EnrolDevice();
        var (_, client) = device.Handshake();
        var envelope = ImFrames.MessageBody(client.Heartbeat(device.NextHeartbeat())).ToArray();
        for (var length = 0; length < envelope.Length; length += 7)
            Assert.False(world.Gate.Verifier.Verify(envelope.AsMemory(0, length)).IsVerified);
        Assert.False(world.Gate.Verifier.Verify(envelope.Concat(new byte[] { 0 }).ToArray()).IsVerified);
    }

    [Fact]
    public void I2_a_verified_message_is_verified_at_most_once()
    {
        var device = world.EnrolDevice();
        var (_, client) = device.Handshake();
        var envelope = ImFrames.MessageBody(client.Heartbeat(device.NextHeartbeat())).ToArray();
        var results = Enumerable.Range(0, 20).Select(_ => world.Gate.Verifier.Verify(envelope)).ToArray();
        Assert.Single(results, r => r.IsVerified);
        Assert.Equal(19, world.Counters.RefusedCount(ImRefusal.Replayed));
    }

    [Fact]
    public void I3_the_refusal_log_interface_admits_only_enumerations()
    {
        var methods = typeof(IImRefusalLog).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.Equal(typeof(void), method.ReturnType);
            Assert.All(method.GetParameters(), p => Assert.True(p.ParameterType.IsEnum, $"{method.Name}({p.Name}) is not an enumeration"));
        }
    }

    [Fact]
    public void No_secret_key_token_or_payload_reaches_anything_the_gate_emits()
    {
        var good = world.EnrolDevice();
        var unenrolled = world.UnenrolledDevice();
        var frames = new List<byte[]>();
        var (connection, client) = good.Handshake();
        connection.Send(client.Heartbeat(new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "canary-agent-7")));
        connection.Send(client.Message("im.reboot", Encoding.ASCII.GetBytes("canary-payload")));
        frames.AddRange(connection.Received);
        var (refused, _) = unenrolled.Handshake();
        frames.AddRange(refused.Received);
        var scanner = world.Connect();
        scanner.Send(Encoding.ASCII.GetBytes("canary-probe"));
        frames.AddRange(scanner.Received);

        // What the gate sent: accept frames (challenge, session id, the gate's own public chain), acks (message ids), refusals (one byte).
        foreach (var frame in frames)
        {
            Assert.False(Contains(frame, world.KnockSecret), "the knock secret left the gate");
            Assert.False(Contains(frame, good.Token), "a token was echoed");
            Assert.False(Contains(frame, Encoding.ASCII.GetBytes("canary")), "content was echoed");
        }
        Assert.All(frames.Where(f => f[0] == (byte)ImFrameType.Refused), f => Assert.Equal(ImFrames.Refused(), f));

        // What the gate counted: reasons and numbers only.
        var counted = string.Join("\n", world.Counters.Snapshot().Select(row => $"{row.What} {row.Count}"));
        Assert.DoesNotContain(good.Subject, counted, StringComparison.Ordinal);
        Assert.DoesNotContain(unenrolled.Subject, counted, StringComparison.Ordinal);
        Assert.DoesNotContain("canary", counted, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(world.KnockSecret), world.Settings.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refused UnknownKind 1", counted, StringComparison.Ordinal);
        Assert.Contains("refused NotEnrolled 1", counted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_errors_carry_the_code_and_never_the_bytes()
    {
        var error = Assert.Throws<ImFormatException>(() => ImTokenCodec.Parse(Encoding.ASCII.GetBytes("canary-token-bytes")));
        Assert.Equal("Malformed", error.Message);
    }

    [Fact]
    public void Every_setting_has_a_key_a_default_and_a_description_and_the_secret_is_marked()
    {
        var settings = MilkyWaySettings.Describe();
        Assert.All(settings, s =>
        {
            Assert.StartsWith("milkyway.", s.Key, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(s.Default));
            Assert.True(s.Description.Length > 20);
        });
        Assert.Equal(settings.Count, settings.Select(s => s.Key).Distinct().Count());
        Assert.True(settings.Single(s => s.Key == "milkyway.im.knockSecrets").IsSecret);
    }

    static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
