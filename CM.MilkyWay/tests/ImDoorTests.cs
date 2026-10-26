using Im;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests;

/// <summary>A door that answers what a test tells it to, and remembers what it was given.</summary>
public sealed class ScriptedDoor(Func<byte[], byte[]?> answer) : IImDoor
{
    public List<byte[]> Seen { get; } = [];

    public byte[]? Answer(ReadOnlyMemory<byte> payload)
    {
        var bytes = payload.ToArray();
        Seen.Add(bytes);
        return answer(bytes);
    }
}

/// <summary>
/// The enrolment door (DMP chapter 04, "The enrolment door"): after the accept and before the response, a machine without a device
/// certificate or a token can pass opaque payloads to Doppler. The gate only moves them, counts them and bounds them; it never
/// verifies, delivers or puts on the board anything that came through the door.
/// </summary>
[Trait("Category", "im")]
public sealed class ImDoorTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly ScriptedDoor door = new(p => [.. "echo:"u8, .. p]);
    readonly SandboxWorld world;

    public ImDoorTests() => world = new SandboxWorld(Start, door: door);

    public void Dispose() => world.Dispose();

    (SimulatedConnection Connection, ImClientSession Client) Accepted(SandboxWorld w)
    {
        var connection = w.Connect();
        var client = new ImClientSession(w.Anchors, w.Clock);
        var accept = connection.Send(client.Knock(w.KnockSecret));
        Assert.False(accept.Close);
        client.CheckGate(accept.Frame!);
        return (connection, client);
    }

    [Fact]
    public void A_machine_without_a_certificate_passes_a_payload_through_the_door_and_gets_Dopplers_answer()
    {
        var (connection, client) = Accepted(world);

        var reply = connection.Send(client.Door("hello"u8));

        Assert.False(reply.Close);
        Assert.Equal("echo:hello"u8.ToArray(), ImClientSession.ReadDoorReply(reply.Frame));
        Assert.Equal("hello"u8.ToArray(), Assert.Single(door.Seen));
        Assert.Equal(1, world.Counters.AcceptedCount(ImAccepted.DoorFrame));
        Assert.False(connection.Session.IsEstablished);
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void After_the_door_the_same_session_can_answer_the_challenge_with_a_token_it_obtained_there()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Accepted(world);
        Assert.False(connection.Send(client.Door("token please"u8)).Close);

        var established = connection.Send(client.Respond(device.Token, device.Key));

        Assert.Equal(ImFrameType.Established, ImFrames.TypeOf(established.Frame));
        client.Established(established.Frame!);
        Assert.False(connection.Send(client.Heartbeat(new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "0.1.0"))).Close);
        Assert.True(Assert.Single(world.Board.Entries).Verified);
    }

    [Fact]
    public void A_door_frame_before_the_knock_gets_the_quiet_answer()
    {
        var connection = world.Connect();

        var reply = connection.Send(ImFrames.Door("x"u8));

        Assert.True(reply.IsQuiet);
        Assert.Empty(door.Seen);
    }

    [Fact]
    public void A_door_frame_inside_an_established_session_is_refused_as_out_of_order()
    {
        var device = world.EnrolDevice();
        var (connection, _) = device.Handshake();
        Assert.True(connection.Session.IsEstablished);
        var before = world.Counters.RefusedCount(ImRefusal.OutOfOrder);

        var reply = connection.Send(ImFrames.Door("x"u8));

        Assert.True(reply.Close);
        Assert.Equal(before + 1, world.Counters.RefusedCount(ImRefusal.OutOfOrder));
        Assert.Empty(door.Seen);
    }

    [Fact]
    public void Dopplers_refusal_closes_the_session_with_the_refused_frame_and_no_reason()
    {
        using var refusing = new SandboxWorld(Start, door: new ScriptedDoor(_ => null));
        var (connection, client) = Accepted(refusing);

        var reply = connection.Send(client.Door("enrol me"u8));

        Assert.True(reply.Close);
        Assert.Equal(ImFrames.Refused(), reply.Frame);
        Assert.Equal(1, refusing.Counters.RefusedCount(ImRefusal.DoorRefused));
        Assert.Throws<ImClientException>(() => ImClientSession.ReadDoorReply(reply.Frame));
    }

    [Fact]
    public void A_gate_without_a_door_refuses_door_frames_as_closed()
    {
        using var closed = new SandboxWorld(Start);
        var (connection, client) = Accepted(closed);

        var reply = connection.Send(client.Door("x"u8));

        Assert.True(reply.Close);
        Assert.Equal(1, closed.Counters.RefusedCount(ImRefusal.DoorClosed));
    }

    [Fact]
    public void More_door_frames_than_the_setting_allows_are_refused_and_Doppler_never_sees_the_extra_one()
    {
        using var small = new SandboxWorld(Start, new MilkyWaySettings { MaxDoorFrames = 2 }, door);
        var (connection, client) = Accepted(small);
        Assert.False(connection.Send(client.Door("1"u8)).Close);
        Assert.False(connection.Send(client.Door("2"u8)).Close);

        var reply = connection.Send(client.Door("3"u8));

        Assert.True(reply.Close);
        Assert.Equal(1, small.Counters.RefusedCount(ImRefusal.DoorLimit));
        Assert.Equal(2, door.Seen.Count);
    }

    [Fact]
    public void A_door_payload_over_the_limit_is_refused_as_malformed_before_Doppler_sees_it()
    {
        using var small = new SandboxWorld(Start, new MilkyWaySettings { MaxDoorBytes = 16 }, door);
        var (connection, client) = Accepted(small);

        var reply = connection.Send(client.Door(new byte[17]));

        Assert.True(reply.Close);
        Assert.Equal(1, small.Counters.RefusedCount(ImRefusal.Malformed));
        Assert.Empty(door.Seen);
    }

    [Fact]
    public void An_answer_longer_than_the_limit_is_refused_rather_than_sent()
    {
        using var loud = new SandboxWorld(Start, new MilkyWaySettings { MaxDoorBytes = 16 }, new ScriptedDoor(_ => new byte[17]));
        var (connection, client) = Accepted(loud);

        var reply = connection.Send(client.Door("x"u8));

        Assert.True(reply.Close);
        Assert.Equal(1, loud.Counters.RefusedCount(ImRefusal.DoorRefused));
    }

    [Fact]
    public void The_handshake_deadline_also_covers_the_door()
    {
        var (connection, client) = Accepted(world);
        world.Clock.Advance(world.Settings.HandshakeTimeout + TimeSpan.FromMilliseconds(1));

        var reply = connection.Send(client.Door("late"u8));

        Assert.True(reply.Close);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.HandshakeTimeout));
        Assert.Empty(door.Seen);
    }

    [Fact]
    public void A_door_frame_with_trailing_bytes_is_malformed()
    {
        var (connection, _) = Accepted(world);

        var reply = connection.Send([.. ImFrames.Door("x"u8), 0]);

        Assert.True(reply.Close);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Malformed));
        Assert.Empty(door.Seen);
    }

    [Fact]
    public void The_client_sends_no_door_frame_before_it_has_checked_the_gate()
    {
        var client = new ImClientSession(world.Anchors, world.Clock);
        client.Knock(world.KnockSecret);

        Assert.Equal(ImClientError.OutOfOrder, Assert.Throws<ImClientException>(() => client.Door("x"u8)).Error);
    }
}
