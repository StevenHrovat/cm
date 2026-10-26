using cm.os.Health;
using Im;
using Im.Trust;
using CM.MilkyWay.Health;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests;

/// <summary>Milky Way's health report for Doppler (DMP chapter 05, "The contract with Doppler"): signed by the gate key, counts and token ids only.</summary>
[Trait("Category", "im")]
public sealed class MilkyWayHealthTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxWorld world = new(Start);

    public void Dispose() => world.Dispose();

    PeerWatch DopplersWatch() => new(MilkyWayHealth.ComponentName, TrustRole.MilkyWayGate, world.Anchors, world.Clock, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));

    [Fact]
    public void The_report_is_signed_by_the_gate_and_passes_Dopplers_watch()
    {
        var watch = DopplersWatch();
        Assert.Equal(ReportRefusal.None, watch.Observe(world.Health.Report()));
        world.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(ReportRefusal.None, watch.Observe(world.Health.Report()));
        Assert.Equal(PeerState.Healthy, watch.View().State);
        Assert.Equal(2, watch.View().Last!.Sequence);
    }

    [Fact]
    public void The_report_lists_the_tokens_accepted_since_the_last_report_and_then_starts_again()
    {
        var device = world.EnrolDevice();
        var (connection, client) = device.Handshake();
        connection.Send(client.Heartbeat(device.NextHeartbeat()));
        var tokenId = Convert.ToHexStringLower(ImTokenCodec.Parse(device.Token).TokenId);

        var first = world.Health.Report().Report;
        Assert.Equal([tokenId], first.AcceptedTokenIds);
        Assert.Equal(1, first.Counts["board.entries"]);
        Assert.Equal(1, first.Counts["accepted.session"]);
        Assert.Equal(1, first.Counts["accepted.heartbeat"]);

        Assert.Empty(world.Health.Report().Report.AcceptedTokenIds);
    }

    [Fact]
    public void A_refused_token_is_never_listed_and_refusals_are_counted_by_reason()
    {
        var device = world.UnenrolledDevice();
        device.Handshake();
        var report = world.Health.Report().Report;
        Assert.Empty(report.AcceptedTokenIds);
        Assert.Equal(1, report.Counts["refused.notenrolled"]);
    }

    [Fact]
    public void The_report_carries_no_subject_and_no_content()
    {
        var device = world.EnrolDevice();
        var (connection, client) = device.Handshake();
        connection.Send(client.Heartbeat(new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "canary-agent")));
        var report = world.Health.Report().Report;
        var text = System.Text.Encoding.UTF8.GetString(report.CanonicalBytes());
        Assert.DoesNotContain(device.Subject, text, StringComparison.Ordinal);
        Assert.DoesNotContain("canary", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_replay_memory_makes_the_report_degraded_until_it_recovers()
    {
        using var small = new SandboxWorld(Start, new MilkyWaySettings { ReplayCapacity = 1 });
        var device = small.EnrolDevice();
        var (connection, client) = device.Handshake();
        connection.Send(client.Heartbeat(device.NextHeartbeat()));
        var (again, client2) = device.Handshake(); // the knock memory is full too: refused, counted
        Assert.False(again.Session.IsEstablished);
        Assert.Equal(HealthState.Degraded, small.Health.Report().Report.State);
        Assert.Equal(HealthState.Healthy, small.Health.Report().Report.State);
    }
}
