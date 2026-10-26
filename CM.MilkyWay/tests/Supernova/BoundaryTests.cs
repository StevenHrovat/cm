using Im;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests.Supernova;

/// <summary>
/// Every limit of the verify rules and the handshake, tested on both sides of its edge, exactly as DMP chapter 04 words it ("no longer
/// than", "not before", "before", "within"). Mutation testing found these edges unpinned: a test one minute away from an edge passes for
/// "<" and "<=" alike. Each test names the rule.
/// </summary>
[Trait("Category", "im")]
public sealed class BoundaryTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxWorld world = new(Start);

    public void Dispose() => world.Dispose();

    TimeSpan Skew => world.Settings.ClockSkew;

    static ImHeartbeat Beat() => new(1, ImHeartbeatStatus.Healthy, "0.1.0");

    VerifyResult Verify(SimulatedDevice device, byte[]? token = null, DateTimeOffset? sentAt = null) =>
        world.Gate.Verifier.Verify(ImEnvelopeCodec.Seal(ImKinds.Heartbeat, Beat().Encode(), token ?? device.Token, device.Key, sentAt ?? world.Clock.GetUtcNow(), world.Settings.MaxPayloadBytes));

    [Fact]
    public void Rule_1_a_frame_of_exactly_the_limit_passes_rule_1_and_one_byte_more_does_not()
    {
        Assert.Equal(ImRefusal.Malformed, world.Gate.Verifier.Verify(new byte[world.Settings.MaxFrameBytes]).Refusal);
        Assert.Equal(ImRefusal.Oversize, world.Gate.Verifier.Verify(new byte[world.Settings.MaxFrameBytes + 1]).Refusal);

        var exact = world.Connect();
        Assert.NotEqual(ImRefusal.Oversize, Counted(() => exact.Send(new byte[world.Settings.MaxFrameBytes])));
        var over = world.Connect();
        Assert.Equal(ImRefusal.Oversize, Counted(() => over.Send(new byte[world.Settings.MaxFrameBytes + 1])));
    }

    [Fact]
    public void Rule_6_a_token_of_exactly_the_longest_lifetime_is_accepted_and_one_millisecond_more_is_not()
    {
        var device = world.EnrolDevice();
        var nb = Start.AddMinutes(-1);
        Assert.True(Verify(device, world.IssueToken(device, notBefore: nb, expiresAt: nb + world.Settings.TokenMaxLifetime)).IsVerified);
        Assert.Equal(ImRefusal.TokenPolicy, Verify(device, world.IssueToken(device, notBefore: nb, expiresAt: nb + world.Settings.TokenMaxLifetime + TimeSpan.FromMilliseconds(1))).Refusal);
    }

    [Fact]
    public void Rule_6_a_token_whose_expiry_equals_its_start_is_not_a_policy_breach_but_is_expired()
    {
        var device = world.EnrolDevice();
        Assert.Equal(ImRefusal.TokenExpired, Verify(device, world.IssueToken(device, notBefore: Start, expiresAt: Start)).Refusal);
    }

    [Fact]
    public void Rule_7_not_before_minus_the_skew_is_the_first_accepted_moment()
    {
        var device = world.EnrolDevice();
        Assert.True(Verify(device, world.IssueToken(device, notBefore: Start + Skew, expiresAt: Start.AddHours(1))).IsVerified);
        Assert.Equal(ImRefusal.TokenNotYetValid, Verify(device, world.IssueToken(device, notBefore: Start + Skew + TimeSpan.FromMilliseconds(1), expiresAt: Start.AddHours(1))).Refusal);
    }

    [Fact]
    public void Rule_8_the_expiry_moment_itself_is_refused_and_the_millisecond_before_is_accepted()
    {
        var device = world.EnrolDevice();
        Assert.True(Verify(device, world.IssueToken(device, notBefore: Start.AddHours(-1), expiresAt: Start.AddMilliseconds(1))).IsVerified);
        Assert.Equal(ImRefusal.TokenExpired, Verify(device, world.IssueToken(device, notBefore: Start.AddHours(-1), expiresAt: Start)).Refusal);
    }

    [Fact]
    public void Rule_12_a_message_exactly_one_skew_old_or_ahead_is_fresh_and_one_millisecond_more_is_stale()
    {
        var device = world.EnrolDevice();
        var ms = TimeSpan.FromMilliseconds(1);
        Assert.True(Verify(device, sentAt: Start - Skew).IsVerified);
        Assert.True(Verify(device, sentAt: Start + Skew).IsVerified);
        Assert.Equal(ImRefusal.Stale, Verify(device, sentAt: Start - Skew - ms).Refusal);
        Assert.Equal(ImRefusal.Stale, Verify(device, sentAt: Start + Skew + ms).Refusal);
    }

    [Fact]
    public void Rule_16_an_id_is_still_remembered_at_sent_at_plus_the_skew()
    {
        var device = world.EnrolDevice();
        var envelope = ImEnvelopeCodec.Seal(ImKinds.Heartbeat, Beat().Encode(), device.Token, device.Key, Start, world.Settings.MaxPayloadBytes);
        Assert.True(world.Gate.Verifier.Verify(envelope).IsVerified);
        world.Clock.Advance(Skew); // the last moment rule 12 still accepts it
        Assert.Equal(ImRefusal.Replayed, world.Gate.Verifier.Verify(envelope).Refusal);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(ImRefusal.Stale, world.Gate.Verifier.Verify(envelope).Refusal);
    }

    [Fact]
    public void Rule_15_only_a_heartbeat_payload_is_parsed_as_a_heartbeat()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, permissions: [.. SandboxWorld.DefaultPermissions, "im.other"]);
        var verified = Verify(device);
        Assert.True(verified.IsVerified);
        Assert.NotNull(verified.Message!.Heartbeat);
    }

    [Fact]
    public void Handshake_a_knock_exactly_one_skew_old_or_ahead_is_fresh_and_one_millisecond_more_is_stale()
    {
        var ms = TimeSpan.FromMilliseconds(1);
        Assert.False(world.Connect().Send(ImFrames.Knock(world.KnockSecret, Start - Skew)).IsQuiet);
        Assert.False(world.Connect().Send(ImFrames.Knock(world.KnockSecret, Start + Skew)).IsQuiet);
        Assert.Equal(ImRefusal.KnockStale, Counted(() => world.Connect().Send(ImFrames.Knock(world.KnockSecret, Start - Skew - ms))));
        Assert.Equal(ImRefusal.KnockStale, Counted(() => world.Connect().Send(ImFrames.Knock(world.KnockSecret, Start + Skew + ms))));
    }

    [Fact]
    public void Handshake_a_response_at_the_deadline_is_accepted_and_one_millisecond_later_is_not()
    {
        foreach (var (late, established) in new[] { (TimeSpan.Zero, true), (TimeSpan.FromMilliseconds(1), false) })
        {
            var device = world.EnrolDevice();
            var connection = world.Connect();
            var client = device.NewClient();
            var accept = connection.Send(client.Knock(world.KnockSecret));
            world.Clock.Advance(world.Settings.HandshakeTimeout + late);
            connection.Send(client.Respond(accept.Frame!));
            Assert.Equal(established, connection.Session.IsEstablished);
            world.Clock.Advance(-(world.Settings.HandshakeTimeout + late));
        }
    }

    [Fact]
    public void Handshake_a_knock_of_the_right_length_but_another_type_byte_gets_the_quiet_answer()
    {
        var knock = ImFrames.Knock(world.KnockSecret, Start);
        knock[0] = (byte)ImFrameType.Response;
        Assert.Equal(ImRefusal.KnockRefused, Counted(() => Assert.True(world.Connect().Send(knock).IsQuiet)));
    }

    [Fact]
    public void Handshake_an_expired_device_certificate_is_counted_as_expired_not_as_foreign()
    {
        var device = world.EnrolDevice();
        device.Key = world.Authority.Issue(Im.Trust.TrustRole.Device, device.Subject, Start.AddDays(-30), Start.AddDays(-1));
        Assert.Equal(ImRefusal.CertificateExpired, Counted(() => device.Handshake()));
    }

    [Fact]
    public void In_a_session_a_frame_that_is_not_a_message_is_refused_as_out_of_order()
    {
        var device = world.EnrolDevice();
        var (connection, _) = device.Handshake();
        Assert.Equal(ImRefusal.OutOfOrder, Counted(() => connection.Send(ImFrames.Ack(new byte[16]))));
    }

    [Fact]
    public void The_quiet_reply_is_no_frame_and_close_and_nothing_else()
    {
        Assert.True(new CM.MilkyWay.Gate.GateReply(null, true).IsQuiet);
        Assert.False(new CM.MilkyWay.Gate.GateReply(null, false).IsQuiet);
        Assert.False(new CM.MilkyWay.Gate.GateReply([1], true).IsQuiet);
    }

    /// <summary>Runs an action and returns the one refusal reason it added to the counters.</summary>
    ImRefusal Counted(Action action)
    {
        var before = Enum.GetValues<ImRefusal>().ToDictionary(r => r, world.Counters.RefusedCount);
        action();
        return Assert.Single(Enum.GetValues<ImRefusal>(), r => world.Counters.RefusedCount(r) > before[r]);
    }
}
