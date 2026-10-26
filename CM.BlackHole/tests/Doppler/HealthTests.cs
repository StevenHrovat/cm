using CM.BlackHole.Doppler;
using cm.os.Health;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace CM.BlackHole.Tests.Doppler;

/// <summary>Doppler and Milky Way watch each other. Milky Way is played here by a signer with the MilkyWayGate role; the pair test uses the real gate.</summary>
public sealed class HealthTests : IDisposable
{
    readonly DopplerWorld world = new();
    readonly IImSigner gate;
    long sequence;

    public HealthTests() => gate = world.Authority.Issue(TrustRole.MilkyWayGate, "milkyway", DopplerWorld.Start.AddDays(-1), DopplerWorld.Start.AddDays(364));

    public void Dispose() => world.Dispose();

    SignedHealthReport MilkyWay(IReadOnlyList<string>? accepted = null, IImSigner? signer = null, long? seq = null, string component = "milkyway") =>
        SignedHealthReport.Sign(new HealthReport(component, HealthState.Healthy, world.Clock.GetUtcNow(), seq ?? ++sequence,
            new Dictionary<string, long> { ["im.accepted"] = accepted?.Count ?? 0 }, accepted ?? []), signer ?? gate);

    [Fact]
    public void Doppler_sees_a_healthy_Milky_Way_and_Milky_Way_can_verify_Dopplers_report()
    {
        Assert.Equal(PeerState.Unknown, world.Server.MilkyWay.State);
        Assert.Equal(ReportRefusal.None, world.Server.ObserveMilkyWay(MilkyWay()));
        Assert.Equal(PeerState.Healthy, world.Server.MilkyWay.State);

        var milkyWaysWatch = new PeerWatch(DopplerServer.ComponentName, TrustRole.DopplerServer, world.Authority.Anchors(), world.Clock, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));
        Assert.Equal(ReportRefusal.None, milkyWaysWatch.Observe(world.Server.Report()));
        Assert.Equal(PeerState.Healthy, milkyWaysWatch.View().State);
    }

    [Fact]
    public void A_token_Milky_Way_accepted_that_Doppler_never_issued_raises_an_alarm()
    {
        var (agent, _, _) = world.NewMachine();
        agent.Start();
        agent.Enrol();
        agent.RefreshToken();

        world.Server.ObserveMilkyWay(MilkyWay([agent.Token!.TokenId]));
        Assert.Equal(HealthState.Healthy, world.Server.Report().Report.State);

        world.Server.ObserveMilkyWay(MilkyWay(["tok-forged"]));
        Assert.Equal(1, world.Server.Counts["alarm.milkyway.unknown_token"]);
        Assert.Equal(HealthState.Unhealthy, world.Server.Report().Report.State);
    }

    [Fact]
    public void A_report_signed_by_the_wrong_role_or_a_foreign_authority_is_refused_and_Milky_Way_counts_as_untrusted()
    {
        Assert.Equal(ReportRefusal.Chain, world.Server.ObserveMilkyWay(MilkyWay(signer: world.Identity)));
        Assert.Equal(PeerState.Untrusted, world.Server.MilkyWay.State);
        Assert.Equal(ChainRefusal.WrongRole, world.Server.MilkyWay.LastChainRefusal);

        using var foreign = new SandboxAuthority("foreign", DopplerWorld.Start.AddMonths(-1));
        var stranger = foreign.Issue(TrustRole.MilkyWayGate, "milkyway", DopplerWorld.Start.AddDays(-1), DopplerWorld.Start.AddDays(10));
        Assert.Equal(ReportRefusal.Chain, world.Server.ObserveMilkyWay(MilkyWay(signer: stranger)));
        Assert.Equal(ChainRefusal.UntrustedRoot, world.Server.MilkyWay.LastChainRefusal);
        Assert.Equal(HealthState.Unhealthy, world.Server.Report().Report.State);

        Assert.Equal(ReportRefusal.None, world.Server.ObserveMilkyWay(MilkyWay()));
        Assert.Equal(PeerState.Healthy, world.Server.MilkyWay.State);
    }

    [Fact]
    public void A_replayed_a_tampered_or_a_misnamed_report_is_refused()
    {
        var first = MilkyWay();
        Assert.Equal(ReportRefusal.None, world.Server.ObserveMilkyWay(first));
        Assert.Equal(ReportRefusal.Replay, world.Server.ObserveMilkyWay(first));

        var tampered = MilkyWay() with { };
        tampered = tampered with { Report = tampered.Report with { State = HealthState.Unhealthy } };
        Assert.Equal(ReportRefusal.Signature, world.Server.ObserveMilkyWay(tampered));

        Assert.Equal(ReportRefusal.WrongComponent, world.Server.ObserveMilkyWay(MilkyWay(component: "not-milkyway")));
    }

    [Fact]
    public void Milky_Way_turns_stale_when_its_reports_stop()
    {
        world.Server.ObserveMilkyWay(MilkyWay());
        world.Clock.Advance(world.Settings.PeerStaleAfter + TimeSpan.FromSeconds(1));
        Assert.Equal(PeerState.Stale, world.Server.MilkyWay.State);
        Assert.Equal(HealthState.Degraded, world.Server.Report().Report.State);
    }

    [Fact]
    public void A_report_from_the_future_is_refused()
    {
        var ahead = SignedHealthReport.Sign(new HealthReport("milkyway", HealthState.Healthy, world.Clock.GetUtcNow().AddMinutes(5), 1,
            new Dictionary<string, long>(), []), gate);
        Assert.Equal(ReportRefusal.FromTheFuture, world.Server.ObserveMilkyWay(ahead));
    }
}
