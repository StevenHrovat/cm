using System.Security.Cryptography;
using Im;
using Im.Trust;
using Im.Trust.Sandbox;
using CM.MilkyWay.Board;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Health;
using CM.MilkyWay.Transport;
using CM.MilkyWay.Verify;

namespace CM.MilkyWay.Simulation;

/// <summary>
/// The sandbox world of IM version 0 (DMP chapter 05, "The simulator"): a sandbox certificate authority standing in for the new CA,
/// Milky Way's gate with its key, Doppler's token signer, an in-memory enrolment, the heartbeat board and the counters, all on
/// simulated time. Nothing in it touches a network or a real system. A second authority stands in for any foreign CA (the old CA
/// included), whose certificates must be refused.
/// </summary>
public sealed class SandboxWorld : IDisposable
{
    public SandboxWorld(DateTimeOffset start, MilkyWaySettings? settings = null, IImDoor? door = null)
    {
        Clock = new SimulatedClock(start);
        Authority = new SandboxAuthority("sandbox", start.AddDays(-30));
        Foreign = new SandboxAuthority("foreign", start.AddDays(-30));
        KnockSecret = RandomNumberGenerator.GetBytes(32);
        var configured = settings ?? new MilkyWaySettings();
        Settings = new MilkyWaySettings
        {
            ClockSkew = configured.ClockSkew,
            TokenMaxLifetime = configured.TokenMaxLifetime,
            HandshakeTimeout = configured.HandshakeTimeout,
            MaxFrameBytes = configured.MaxFrameBytes,
            MaxPayloadBytes = configured.MaxPayloadBytes,
            ReplayCapacity = configured.ReplayCapacity,
            MaxDoorFrames = configured.MaxDoorFrames,
            MaxDoorBytes = configured.MaxDoorBytes,
            MaxConnections = configured.MaxConnections,
            MaxUncertifiedConnections = configured.MaxUncertifiedConnections,
            IdleTimeout = configured.IdleTimeout,
            KnockSecrets = configured.KnockSecrets.Count > 0 ? configured.KnockSecrets : [KnockSecret],
        };
        Anchors = Authority.Anchors();
        GateKey = Authority.Issue(TrustRole.MilkyWayGate, "milky-way-gate", start.AddDays(-1), start.AddDays(365));
        TokenSigner = Authority.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", start.AddDays(-1), start.AddDays(365));
        Health = new MilkyWayHealth(GateKey, Counters, Board, Clock);
        Gate = new MilkyWayGate(Settings, GateKey, Anchors, Enrolment, Clock, Counters, Health, door);
        Transport = new GateTransport(Gate);
    }

    public SimulatedClock Clock { get; }
    public SandboxAuthority Authority { get; }
    public SandboxAuthority Foreign { get; }
    public ITrustAnchors Anchors { get; }
    public IImSigner GateKey { get; }
    public IImSigner TokenSigner { get; }
    public SandboxEnrolmentRegistry Enrolment { get; } = new();
    public ImCounters Counters { get; } = new();
    public HeartbeatBoard Board { get; } = new();

    /// <summary>Milky Way's signed health report for Doppler; the gate's sink, in front of <see cref="Board"/>.</summary>
    public MilkyWayHealth Health { get; }

    public MilkyWaySettings Settings { get; }
    public MilkyWayGate Gate { get; }

    /// <summary>The gate on a stream: TLS 1.3 with mutual certificates, then frames (DMP chapter 05, "Transport encryption").</summary>
    public GateTransport Transport { get; }

    /// <summary>The shared secret of the knock in this world (random per world, never written anywhere).</summary>
    public byte[] KnockSecret { get; }

    /// <summary>The permissions Doppler gives every enrolled role in IM version 0.</summary>
    public static IReadOnlyCollection<string> DefaultPermissions { get; } = [ImKinds.SessionPermission, ImKinds.HeartbeatPermission];

    /// <summary>A device with a Device certificate, enrolled with Doppler, holding a valid token.</summary>
    public SimulatedDevice EnrolDevice(EnrolledRole role = EnrolledRole.Laptop)
    {
        var device = NewDevice(role);
        Enrolment.Put(new EnrolmentRecord(device.Subject, role, device.Key.KeyId, false));
        device.Token = IssueToken(device);
        return device;
    }

    /// <summary>A device with a valid certificate and a validly signed token, but unknown to Doppler's enrolment.</summary>
    public SimulatedDevice UnenrolledDevice()
    {
        var device = NewDevice(EnrolledRole.Laptop);
        device.Token = IssueToken(device);
        return device;
    }

    /// <summary>A token for a device; every part can be overridden to build expired, not-yet-valid, wrong-permission or forged tokens.</summary>
    public byte[] IssueToken(SimulatedDevice device, IReadOnlyCollection<string>? permissions = null, DateTimeOffset? notBefore = null, DateTimeOffset? expiresAt = null,
        IImSigner? signer = null, EnrolledRole? role = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        var now = Clock.GetUtcNow();
        var nb = notBefore ?? now.AddMinutes(-1);
        var claims = new ImTokenClaims(device.Subject, role ?? device.Role, ImKeys.SubjectPublicKeyInfo(device.Key.Certificate), permissions ?? DefaultPermissions, nb, expiresAt ?? nb.AddHours(8));
        return ImTokenCodec.Issue(claims, signer ?? TokenSigner, now);
    }

    /// <summary>A new frame-only connection to the gate (no transport: frames are handed over in memory).</summary>
    public SimulatedConnection Connect() => new(Gate.Open());

    /// <summary>
    /// A new stream connection to the gate's transport, in memory: the device's end, and the task that serves the gate's end (it ends
    /// when the gate has closed the connection). Nothing touches a network.
    /// </summary>
    public (Stream DeviceEnd, Task Served) ConnectStream(CancellationToken cancellationToken = default)
    {
        var (deviceEnd, gateEnd) = InMemoryDuplex.Pair();
        return (deviceEnd, Transport.ServeAsync(gateEnd, cancellationToken));
    }

    SimulatedDevice NewDevice(EnrolledRole role)
    {
        var now = Clock.GetUtcNow();
        var subject = "d-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var key = Authority.Issue(TrustRole.Device, subject, now.AddDays(-1), now.AddDays(365));
        return new SimulatedDevice(this, subject, role, key);
    }

    public void Dispose()
    {
        Transport.Dispose();
        Authority.Dispose();
        Foreign.Dispose();
    }
}

/// <summary>One simulated connection: frames in, the gate's replies out, everything the gate sent recorded.</summary>
public sealed class SimulatedConnection(GateSession session)
{
    readonly List<byte[]> received = [];

    public GateSession Session => session;

    /// <summary>Every frame the gate sent on this connection, in order.</summary>
    public IReadOnlyList<byte[]> Received => received;

    public GateReply Send(byte[] frame)
    {
        var reply = session.Receive(frame);
        if (reply.Frame is { } sent) received.Add(sent);
        return reply;
    }

    /// <summary>
    /// Runs the handshake for any client (a <see cref="SimulatedDevice"/>, or Doppler's agent in the pair test, which brings its own
    /// key and token). True when the session is established; on a refusal the gate's counters say why.
    /// </summary>
    public bool Handshake(ImClientSession client, ReadOnlySpan<byte> knockSecret)
    {
        ArgumentNullException.ThrowIfNull(client);
        var accept = Send(client.Knock(knockSecret));
        if (accept.Frame is null || accept.Close) return false;
        var established = Send(client.Respond(accept.Frame));
        if (established.Frame is not { } frame || established.Close) return false;
        client.Established(frame);
        return true;
    }
}

/// <summary>A simulated device: its opaque subject, its Device key and certificate, and the token Doppler gave it.</summary>
public sealed class SimulatedDevice(SandboxWorld world, string subject, EnrolledRole role, IImSigner key)
{
    ulong sequence;

    public string Subject => subject;
    public EnrolledRole Role => role;
    public IImSigner Key { get; set; } = key;
    public byte[] Token { get; set; } = [];

    /// <summary>A client session with this device's key and token, verifying the gate against the world's trust anchors.</summary>
    public ImClientSession NewClient() => new(world.Anchors, Key, Token, world.Clock, world.Settings.MaxPayloadBytes);

    /// <summary>Connects and runs the handshake. Returns the connection and client; <see cref="GateSession.IsEstablished"/> says whether it worked.</summary>
    public (SimulatedConnection Connection, ImClientSession Client) Handshake()
    {
        var connection = world.Connect();
        var client = NewClient();
        connection.Handshake(client, world.KnockSecret);
        return (connection, client);
    }

    /// <summary>
    /// Connects over the in-memory stream with TLS 1.3 and this device's certificate, then runs the IM handshake inside it. Throws
    /// <see cref="ImClientException"/> when the gate refuses; <c>Served</c> ends when the gate has closed its end.
    /// </summary>
    public async Task<(ImTlsClientConnection Connection, Task Served)> ConnectTlsAsync(CancellationToken cancellationToken = default)
    {
        var (deviceEnd, served) = world.ConnectStream(cancellationToken);
        try
        {
            var connection = await ImTlsClientConnection.ConnectAsync(deviceEnd, Key, world.Anchors, Token, world.KnockSecret, world.Clock,
                world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, cancellationToken).ConfigureAwait(false);
            return (connection, served);
        }
        catch
        {
            await deviceEnd.DisposeAsync().ConfigureAwait(false);
            await served.ConfigureAwait(false);
            throw;
        }
    }

    public ImHeartbeat NextHeartbeat(ImHeartbeatStatus status = ImHeartbeatStatus.Healthy) => new(++sequence, status, "0.1.0");
}
