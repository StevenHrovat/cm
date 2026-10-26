using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.MilkyWay.Simulation;
using Im;
using Im.Trust;
using Xunit;

namespace Cm.SandboxPair.Tests;

/// <summary>The agent's connections to the simulator's gate: a door without a certificate, and sessions with the device's certificate.</summary>
public sealed class SimulatedGateConnector(PairWorld world) : IGateConnector
{
    public bool Unreachable { get; set; }

    public int DoorsOpened { get; private set; }

    public DoorChannel OpenDoor()
    {
        if (Unreachable) throw new IOException("no route to the gate");
        DoorsOpened++;
        return world.OpenDoor();
    }

    public IGateSession OpenSession(IImSigner device, byte[] token)
    {
        if (Unreachable) throw new IOException("no route to the gate");
        var (connection, client, established) = world.Connect(device, token);
        if (!established) throw new ImClientException(ImClientError.DoorRefused);
        return new Session(connection, client);
    }

    sealed class Session(SimulatedConnection connection, ImClientSession client) : IGateSession
    {
        public void Heartbeat(ImHeartbeat heartbeat)
        {
            var reply = connection.Send(client.Heartbeat(heartbeat));
            if (reply.Frame is null || ImFrames.TypeOf(reply.Frame) != ImFrameType.Ack) throw new ImClientException(ImClientError.DoorRefused);
        }

        public void Dispose()
        {
        }
    }
}

public sealed class MemoryAgentStore : IAgentStore
{
    public AgentRecord? Record { get; set; }

    public AgentRecord? Load() => Record;

    public void Save(AgentRecord record) => Record = record;
}

/// <summary>
/// The agent service's loop (lane L4b goal 3) against Milky Way's real gate and Doppler, in the simulator: it enrols through the door,
/// sends verified heartbeats, keeps its enrolment across a restart, renews its token when due, backs off when refused or cut off.
/// </summary>
[Trait("Category", "pair")]
public sealed class AgentLoopPairTests : IDisposable
{
    readonly PairWorld world = new();
    readonly MemoryAgentStore store = new();
    readonly SimulatedGateConnector gate;
    readonly SimulatedTpm tpm = new(PairWorld.Good);

    public AgentLoopPairTests() => gate = new SimulatedGateConnector(world);

    public void Dispose()
    {
        tpm.Dispose();
        world.Dispose();
    }

    void Plan() => Assert.True(world.Doppler.AddPlan(new EnrolmentPlan("plan-loop", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));

    AgentLoop Loop() => new(tpm, new SimulatedDeviceHost(), gate, store, world.Anchors, world.Clock);

    [Fact]
    public void A_planned_machine_enrols_through_the_door_and_its_heartbeats_arrive_verified()
    {
        Plan();
        var loop = Loop();

        Assert.Equal(TimeSpan.Zero, loop.Step());
        Assert.Equal(AgentOutcome.Enrolled, loop.LastOutcome);
        Assert.Equal(TimeSpan.FromMinutes(1), loop.Step());
        Assert.Equal(AgentOutcome.HeartbeatSent, loop.LastOutcome);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        loop.Step();

        var entry = Assert.Single(world.Board.Entries);
        Assert.Equal(loop.Agent.Subject, entry.Subject);
        Assert.True(entry.Verified);
        Assert.Equal(2UL, store.Record!.Sequence);
        Assert.Equal(2, gate.DoorsOpened); // enrolment, one token; the second heartbeat reused the token
    }

    [Fact]
    public void After_a_restart_the_agent_keeps_its_enrolment_and_its_sequence()
    {
        Plan();
        var first = Loop();
        first.Step();
        first.Step();
        var subject = first.Agent.Subject;

        var second = Loop();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), second.Step());

        Assert.Equal(AgentOutcome.HeartbeatSent, second.LastOutcome);
        Assert.Equal(subject, second.Agent.Subject);
        Assert.Equal(2UL, second.Sequence);
        Assert.Equal(1, world.Doppler.Registry.Count);
    }

    [Fact]
    public void The_token_is_renewed_through_the_door_before_it_runs_out()
    {
        Plan();
        var loop = Loop();
        loop.Step();
        loop.Step();
        var firstToken = loop.Agent.Token!.TokenId;

        world.Clock.Advance(TimeSpan.FromMinutes(56));
        loop.Step();

        Assert.Equal(AgentOutcome.HeartbeatSent, loop.LastOutcome);
        Assert.NotEqual(firstToken, loop.Agent.Token!.TokenId);
    }

    [Fact]
    public void An_unplanned_machine_is_refused_and_backs_off_up_to_the_limit()
    {
        var loop = Loop();

        var waits = Enumerable.Range(0, 6).Select(_ => loop.Step()).ToList();

        Assert.Equal(AgentOutcome.EnrolRefused, loop.LastOutcome);
        Assert.Equal([5, 10, 20, 40, 60, 60], waits.Select(w => (int)w.TotalMinutes));
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void A_revoked_machine_is_refused_at_the_gate_and_its_heartbeat_never_arrives()
    {
        Plan();
        var loop = Loop();
        loop.Step();
        loop.Step();
        var before = world.Board.Find(loop.Agent.Subject!)!.Sequence;

        world.Doppler.Revoke(loop.Agent.Subject!);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        loop.Step();

        Assert.Equal(AgentOutcome.SessionRefused, loop.LastOutcome);
        Assert.Equal(before, world.Board.Find(loop.Agent.Subject!)!.Sequence);
    }

    [Fact]
    public void A_gate_that_cannot_be_reached_is_a_backoff_not_a_crash()
    {
        Plan();
        gate.Unreachable = true;
        var loop = Loop();

        Assert.Equal(TimeSpan.FromMinutes(5), loop.Step());
        Assert.Equal(AgentOutcome.GateUnreachable, loop.LastOutcome);

        gate.Unreachable = false;
        Assert.Equal(TimeSpan.Zero, loop.Step());
        Assert.Equal(AgentOutcome.Enrolled, loop.LastOutcome);
    }
}

/// <summary>
/// The same loop over TLS 1.3 through Milky Way's real gate transport, on an in-memory pipe: the door connection shows no
/// certificate, the message session shows the Device certificate Doppler issued. This is the code path of the Windows service.
/// </summary>
[Trait("Category", "pair")]
public sealed class AgentLoopTlsPairTests : IDisposable
{
    readonly PairWorld world = new();
    readonly CM.MilkyWay.Transport.GateTransport transport;
    readonly SimulatedTpm tpm = new(PairWorld.Good);
    readonly MemoryAgentStore store = new();

    public AgentLoopTlsPairTests() => transport = new CM.MilkyWay.Transport.GateTransport(world.Gate);

    public void Dispose()
    {
        transport.Dispose();
        tpm.Dispose();
        world.Dispose();
    }

    TlsGateConnector Connector() => new(() =>
    {
        var (device, gate) = InMemoryDuplex.Pair();
        _ = transport.ServeAsync(gate);
        return device;
    }, world.Anchors, world.KnockSecret, world.Clock, _ => null);

    // Off the test framework's synchronisation context: the loop waits on TLS synchronously, as the service's worker thread does.
    static T Run<T>(Func<T> step) => Task.Run(step).GetAwaiter().GetResult();

    [Fact]
    public void A_planned_machine_enrols_and_sends_a_verified_heartbeat_over_TLS()
    {
        Assert.True(world.Doppler.AddPlan(new EnrolmentPlan("plan-tls", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        var loop = new AgentLoop(tpm, new SimulatedDeviceHost(), Connector(), store, world.Anchors, world.Clock);

        Run(loop.Step);
        Assert.Equal(AgentOutcome.Enrolled, loop.LastOutcome);
        Run(loop.Step);
        Assert.Equal(AgentOutcome.HeartbeatSent, loop.LastOutcome);

        Assert.True(Assert.Single(world.Board.Entries).Verified);
    }

    [Fact]
    public void An_unplanned_machine_is_refused_at_the_door_over_TLS()
    {
        var loop = new AgentLoop(tpm, new SimulatedDeviceHost(), Connector(), store, world.Anchors, world.Clock);

        Run(loop.Step);

        Assert.Equal(AgentOutcome.EnrolRefused, loop.LastOutcome);
        Assert.Empty(world.Board.Entries);
    }
    [Fact]
    public async Task Over_a_TCP_connection_the_agent_enrols_and_its_heartbeat_arrives_verified()
    {
        Assert.True(world.Doppler.AddPlan(new EnrolmentPlan("plan-tcp", tpm.EndorsementKeyId, EnrolledRole.Test, ["admin-a", "admin-b"])));
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var accepting = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop.Token); }
                catch (OperationCanceledException) { break; }
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try { await transport.ServeAsync(client.GetStream(), stop.Token); }
                        catch (Exception e) when (e is IOException or OperationCanceledException) { }
                    }
                });
            }
        });
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var connector = new TlsGateConnector(() =>
        {
            var client = new System.Net.Sockets.TcpClient();
            client.Connect(System.Net.IPAddress.Loopback, port);
            return client.GetStream();
        }, world.Anchors, world.KnockSecret, world.Clock, _ => null);
        var loop = new AgentLoop(tpm, new SimulatedDeviceHost(), connector, store, world.Anchors, world.Clock);

        await Task.Run(loop.Step);
        Assert.Equal(AgentOutcome.Enrolled, loop.LastOutcome);
        await Task.Run(loop.Step);
        Assert.Equal(AgentOutcome.HeartbeatSent, loop.LastOutcome);
        Assert.True(Assert.Single(world.Board.Entries).Verified);

        await stop.CancelAsync();
        listener.Stop();
        await accepting;
    }
}
