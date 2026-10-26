using CM.BlackHole.Doppler;
using Im;
using Im.Trust;
using CM.MilkyWay.Verify;
using Xunit;

namespace Cm.SandboxPair.Tests;

/// <summary>
/// Lane L4b, goal 1: a machine enrols into a role through Milky Way's enrolment door, not through an object in memory; the register
/// lives in Doppler's store (a file on the Doppler server); a revocation written by the admin command is seen by the verifier at
/// the next message. Group "pair" of tools/verify.
/// </summary>
[Trait("Category", "pair")]
public sealed class DoorPairTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("doppler-pair-").FullName;
    readonly PairWorld world;

    public DoorPairTests() => world = new PairWorld(new FileDopplerStore(folder));

    public void Dispose()
    {
        world.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    static ImHeartbeat Beat(ulong sequence) => new(sequence, ImHeartbeatStatus.Healthy, "0.1.0");

    /// <summary>Enrols through one door connection, then fetches a token and opens the session on a second one (as the agent service does).</summary>
    (DopplerAgent Agent, DoorChannel Session) EnrolAndConnect()
    {
        var (agent, _, door) = world.NewMachineBehindTheDoor();
        Assert.Equal(AgentState.Started, agent.Start());
        door();
        Assert.True(agent.Enrol(), $"enrolment through the door was refused: {agent.LastRefusal}");
        var session = door();
        Assert.True(agent.RefreshToken());
        Assert.True(session.Respond(agent.Token!.Encoded, agent.DeviceSigner!));
        return (agent, session);
    }

    [Fact]
    public void A_planned_machine_enrols_through_the_door_and_its_heartbeat_arrives_verified()
    {
        var (agent, session) = EnrolAndConnect();

        var ack = session.Link.Exchange(session.Client.Heartbeat(Beat(1)));

        Assert.Equal(ImFrameType.Ack, ImFrames.TypeOf(ack));
        var entry = world.Board.Find(agent.Subject!);
        Assert.NotNull(entry);
        Assert.True(entry.Verified);
        Assert.Equal(EnrolledRole.Laptop, entry.Role);
        Assert.True(world.Counters.AcceptedCount(ImAccepted.DoorFrame) >= 4);
    }

    [Fact]
    public void An_unplanned_machine_is_refused_at_the_door_and_never_reaches_the_board()
    {
        var (agent, _, door) = world.NewMachineBehindTheDoor(planned: false);
        agent.Start();
        door();

        Assert.False(agent.Enrol());
        Assert.Equal(DopplerRefusal.Unanswered, agent.LastRefusal); // the agent is told nothing
        Assert.Equal(1, world.Doppler.Counts["enrol.refused.noapprovedplan"]); // Doppler counts why
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.DoorRefused));
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void A_revocation_written_by_the_admin_command_is_seen_by_the_verifier_at_the_next_message()
    {
        var (agent, session) = EnrolAndConnect();
        Assert.Equal(ImFrameType.Ack, ImFrames.TypeOf(session.Link.Exchange(session.Client.Heartbeat(Beat(1)))));

        // The admin command is a second process on the Doppler server: its own store object on the same folder.
        Assert.True(DopplerAdmin.Revoke(new FileDopplerStore(folder), agent.Subject!, world.Clock.GetUtcNow()));

        var reply = session.Link.Exchange(session.Client.Heartbeat(Beat(2)));
        Assert.Equal(ImFrames.Refused(), reply);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Revoked));
        Assert.Equal(1, world.Doppler.SyncRevocations()); // and the certificate follows onto the CRL
        Assert.Equal(ChainRefusal.Revoked, world.Anchors.Validate(agent.DeviceSigner!.Certificate, agent.DeviceSigner.Chain, TrustRole.Device, world.Clock.GetUtcNow()).Refusal);

        // A revoked machine gets no new token through the door either.
        var fresh = world.OpenDoor();
        var challenge = fresh.Challenge();
        Assert.NotNull(challenge);
        var proof = agent.DeviceSigner.Sign(EnrolmentStatements.TokenProof(agent.Subject!, challenge));
        Assert.False(fresh.IssueToken(new TokenRequest(agent.Subject!, challenge, proof)).Ok);
        Assert.Equal(1, world.Doppler.Counts["token.refused.revoked"]);
    }

    [Fact]
    public void The_register_survives_a_restart_of_the_Doppler_server()
    {
        var (agent, _) = EnrolAndConnect();

        using var restarted = new PairWorld(new FileDopplerStore(folder));
        var record = restarted.Doppler.Registry.Find(agent.Subject!);

        Assert.NotNull(record);
        Assert.Equal(agent.DeviceSigner!.KeyId, record.KeyId);
        Assert.False(record.Revoked);
        Assert.Equal(1, restarted.Doppler.Registry.Count);
    }

    [Fact]
    public void A_plan_added_by_the_admin_command_while_the_server_runs_lets_the_machine_enrol()
    {
        var (agent, tpm, door) = world.NewMachineBehindTheDoor(planned: false);
        agent.Start();
        Assert.True(DopplerAdmin.AddPlan(new FileDopplerStore(folder), new DopplerSettings(), new EnrolmentPlan("plan-cli", tpm.EndorsementKeyId, EnrolledRole.Test, ["admin-a", "admin-b"])));

        door();
        Assert.True(agent.Enrol());
        Assert.Equal(EnrolledRole.Test, world.Doppler.Registry.Find(agent.Subject!)!.Role);
        Assert.Empty(new FileDopplerStore(folder).Read().Plans); // used up
    }

    [Fact]
    public void Garbage_through_the_door_is_refused_and_counted_without_content()
    {
        var door = world.OpenDoor();

        var reply = door.Link.Exchange(door.Client.Door("DPQ\0\u0009nonsense"u8));

        Assert.Equal(ImFrames.Refused(), reply);
        Assert.Equal(1, world.Doppler.Counts["door.malformed"]);
        Assert.DoesNotContain(world.Doppler.Counts.Keys, k => k.Contains("nonsense", StringComparison.Ordinal));
    }
}
