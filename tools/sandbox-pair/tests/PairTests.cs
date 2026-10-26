using CM.BlackHole.Doppler;
using CM.MilkyWay.Verify;
using cm.os.Health;
using Im;
using Im.Trust;
using Xunit;

namespace Cm.SandboxPair.Tests;

/// <summary>
/// The acceptance test of lane L4, goal 1 (plan to 1 December): the Doppler agent and Milky Way's gate on the sandbox. An enrolled
/// machine's heartbeat is accepted and shown verified; an unenrolled one is refused at Milky Way; Doppler and Milky Way show each
/// other's health. Group "pair" of tools/verify.
/// </summary>
[Trait("Category", "pair")]
public sealed class PairTests : IDisposable
{
    readonly PairWorld world = new();

    public void Dispose() => world.Dispose();

    static ImHeartbeat Beat(ulong sequence) => new(sequence, ImHeartbeatStatus.Healthy, "0.1.0");

    (DopplerAgent Agent, bool Established, CM.MilkyWay.Simulation.SimulatedConnection Connection, ImClientSession Client) EnrolledAgentConnected()
    {
        var (agent, _) = world.NewMachine();
        Assert.Equal(AgentState.Started, agent.Start());
        Assert.True(agent.Enrol());
        Assert.True(agent.RefreshToken());
        var (connection, client, established) = world.Connect(agent.DeviceSigner!, agent.Token!.Encoded);
        return (agent, established, connection, client);
    }

    [Fact]
    public void An_enrolled_machines_heartbeat_is_accepted_and_shown_verified_on_the_board()
    {
        var (agent, established, connection, client) = EnrolledAgentConnected();
        Assert.True(established);

        var reply = connection.Send(client.Heartbeat(Beat(1)));
        Assert.False(reply.Close);
        var entry = world.Board.Find(agent.Subject!);
        Assert.NotNull(entry);
        Assert.True(entry.Verified);
        Assert.Equal(EnrolledRole.Laptop, entry.Role);
        Assert.Equal(1UL, entry.Sequence);
    }

    [Fact]
    public void A_machine_Doppler_never_enrolled_is_refused_at_Milky_Way_even_with_a_valid_certificate_and_token()
    {
        // Everything is genuine except the enrolment: a Device certificate of the new CA and a token signed by Doppler's own authority key.
        var stray = world.Authority.Issue(TrustRole.Device, "dev-not-enrolled", PairWorld.Start.AddDays(-1), PairWorld.Start.AddDays(30));
        var token = new ImTokenEncoder().Encode(new TokenClaims("dev-not-enrolled", EnrolledRole.Laptop, stray.KeyId, stray.Certificate.PublicKey.ExportSubjectPublicKeyInfo(),
            DopplerServer.PermissionsFor(EnrolledRole.Laptop), PairWorld.Start, PairWorld.Start.AddHours(1)), world.TokenAuthority);

        var (connection, client, established) = world.Connect(stray, token.Encoded);
        if (established) Assert.True(connection.Send(client.Heartbeat(Beat(1))).Close);

        Assert.Null(world.Board.Find("dev-not-enrolled"));
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.NotEnrolled));
    }

    [Fact]
    public void A_machine_Doppler_refused_to_enrol_never_gets_a_token_and_cannot_reach_the_board()
    {
        var (agent, _) = world.NewMachine(planned: false);
        agent.Start();
        Assert.False(agent.Enrol());
        Assert.False(agent.RefreshToken());
        Assert.Null(agent.DeviceSigner);
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void A_machine_Doppler_revokes_is_refused_on_its_next_message()
    {
        var (agent, established, connection, client) = EnrolledAgentConnected();
        Assert.True(established);
        Assert.False(connection.Send(client.Heartbeat(Beat(1))).Close);

        Assert.True(world.Doppler.Revoke(agent.Subject!));
        Assert.True(connection.Send(client.Heartbeat(Beat(2))).Close);
        Assert.Equal(1UL, world.Board.Find(agent.Subject!)!.Sequence);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Revoked));
    }

    [Fact]
    public void A_device_of_a_foreign_authority_is_refused_in_the_handshake()
    {
        // The old CA's stand-in: a well-formed Device certificate under another root, with a token Doppler signed for its key.
        var old = world.Foreign.Issue(TrustRole.Device, "dev-old-ca", PairWorld.Start.AddDays(-1), PairWorld.Start.AddDays(30));
        var token = new ImTokenEncoder().Encode(new TokenClaims("dev-old-ca", EnrolledRole.Laptop, old.KeyId, old.Certificate.PublicKey.ExportSubjectPublicKeyInfo(),
            DopplerServer.PermissionsFor(EnrolledRole.Laptop), PairWorld.Start, PairWorld.Start.AddHours(1)), world.TokenAuthority);

        var (_, _, established) = world.Connect(old, token.Encoded);
        Assert.False(established);
        Assert.Empty(world.Board.Entries);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.UntrustedDevice));
    }

    [Fact]
    public void Doppler_and_Milky_Way_show_each_others_health()
    {
        var (_, established, connection, client) = EnrolledAgentConnected();
        Assert.True(established);
        connection.Send(client.Heartbeat(Beat(1)));

        Assert.Equal(ReportRefusal.None, world.Doppler.ObserveMilkyWay(world.MilkyWayReport()));
        Assert.Equal(ReportRefusal.None, world.MilkyWaysWatchOnDoppler.Observe(world.Doppler.Report()));

        var dopplerSees = world.Doppler.MilkyWay;
        Assert.Equal(PeerState.Healthy, dopplerSees.State);
        Assert.Equal(1, dopplerSees.Last!.Counts["board.entries"]);
        var milkyWaySees = world.MilkyWaysWatchOnDoppler.View();
        Assert.Equal(PeerState.Healthy, milkyWaySees.State);
        Assert.Equal(1, milkyWaySees.Last!.Counts["enrolled"]);
    }

    [Fact]
    public void Each_side_sees_the_other_go_stale_and_refuses_a_report_signed_with_the_wrong_key()
    {
        world.Doppler.ObserveMilkyWay(world.MilkyWayReport());
        world.MilkyWaysWatchOnDoppler.Observe(world.Doppler.Report());
        world.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(PeerState.Stale, world.Doppler.MilkyWay.State);
        Assert.Equal(PeerState.Stale, world.MilkyWaysWatchOnDoppler.View().State);

        // A report "from Doppler" signed with Milky Way's gate key: right signature, wrong role.
        var forged = SignedHealthReport.Sign(world.Doppler.Report().Report with { Sequence = 99 }, world.GateKey);
        Assert.Equal(ReportRefusal.Chain, world.MilkyWaysWatchOnDoppler.Observe(forged));
        Assert.Equal(PeerState.Untrusted, world.MilkyWaysWatchOnDoppler.View().State);
    }

    [Fact]
    public void An_agent_that_lost_port_138_never_enrols_or_reaches_Milky_Way()
    {
        var (agent, host) = world.NewMachine();
        host.TryBind(Transport.Tcp, 138, "something-else");
        Assert.Equal(AgentState.PortConflict, agent.Start());
        Assert.False(agent.Enrol());
        Assert.Equal(0, world.Doppler.Registry.Count);
    }
}
