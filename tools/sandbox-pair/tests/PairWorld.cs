using System.Security.Cryptography;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.MilkyWay;
using CM.MilkyWay.Board;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Health;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using cm.os.Health;
using Im;
using Im.Trust;
using Im.Trust.Sandbox;

namespace Cm.SandboxPair.Tests;

/// <summary>Doppler's token seam, filled with Milky Way's codec (Im): the one place the two formats meet.</summary>
public sealed class ImTokenEncoder : ITokenEncoder
{
    public IssuedToken Encode(TokenClaims claims, IImSigner authority)
    {
        var encoded = ImTokenCodec.Issue(new ImTokenClaims(claims.Subject, claims.Role, claims.DeviceKeySpki, claims.Permissions, claims.IssuedAt, claims.Expires), authority, claims.IssuedAt);
        return new IssuedToken(Convert.ToHexStringLower(ImTokenCodec.Parse(encoded).TokenId), encoded, claims.Expires);
    }
}

/// <summary>The agent's gate link over the simulator's in-memory connection (outside the sandbox: TLS over TCP).</summary>
public sealed class SimulatedLink(SimulatedConnection connection) : IGateLink
{
    public SimulatedConnection Connection => connection;

    public byte[]? Exchange(byte[] frame) => connection.Send(frame).Frame;

    public void Dispose()
    {
    }
}

/// <summary>Lets a test give the agent a fresh door connection for each step, as the agent service does (one connection per attempt).</summary>
public sealed class SwitchableChannel : IDopplerChannel
{
    DoorChannel? current;

    public DoorChannel Current => current ?? throw new InvalidOperationException("no door connection yet");

    public DoorChannel Use(DoorChannel channel) => current = channel;

    public byte[]? Challenge() => Current.Challenge();

    public BeginResult BeginEnrolment(EnrolBegin request) => Current.BeginEnrolment(request);

    public EnrolmentResult FinishEnrolment(EnrolFinish request) => Current.FinishEnrolment(request);

    public TokenResult IssueToken(TokenRequest request) => Current.IssueToken(request);
}

public sealed class NoAudit : IDopplerAudit
{
    public List<AuditLine> Lines { get; } = [];

    public void Write(AuditLine line) => Lines.Add(line);
}

/// <summary>
/// The sandbox pair: one sandbox authority (the new CA's stand-in), Doppler with its token authority and identity, and Milky Way's
/// gate reading Doppler's register, all on one simulated clock. Milky Way's health report comes from CM.MilkyWay's
/// <see cref="MilkyWayHealth"/>, which sits in front of the board as the gate's sink.
/// </summary>
public sealed class PairWorld : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    public const string AgentPackage = "00000000000000000000000000000000000000000000000000000000000000aa";

    readonly List<IDisposable> owned = [];

    public PairWorld(IDopplerStore? store = null)
    {
        Clock = new SimulatedClock(Start);
        Authority = new SandboxAuthority("sandbox", Start.AddDays(-30));
        Foreign = new SandboxAuthority("foreign", Start.AddDays(-30));
        Anchors = Authority.Anchors();
        KnockSecret = RandomNumberGenerator.GetBytes(32);

        TokenAuthority = Authority.Issue(TrustRole.ImTokenSigner, "doppler token authority 1", Start.AddDays(-1), Start.AddDays(89));
        DopplerIdentity = Authority.Issue(TrustRole.DopplerServer, "doppler", Start.AddDays(-1), Start.AddDays(364));
        GateKey = Authority.Issue(TrustRole.MilkyWayGate, "milky-way-gate", Start.AddDays(-1), Start.AddDays(364));

        Doppler = new DopplerServer(new DopplerSettings { AllowedAgentPackages = new HashSet<string>([AgentPackage], StringComparer.Ordinal) },
            Authority, TokenAuthority, DopplerIdentity, new ImTokenEncoder(), Anchors, Audit, Clock, MilkyWayComponent, store);
        Health = new MilkyWayHealth(GateKey, Counters, Board, Clock);
        Gate = new MilkyWayGate(new MilkyWaySettings { KnockSecrets = [KnockSecret] }, GateKey, Anchors, Doppler.Registry, Clock, Counters, Health, new DopplerDoor(Doppler));
        MilkyWaysWatchOnDoppler = new PeerWatch(DopplerServer.ComponentName, TrustRole.DopplerServer, Anchors, Clock, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));
    }

    public const string MilkyWayComponent = MilkyWayHealth.ComponentName;

    public SimulatedClock Clock { get; }
    public SandboxAuthority Authority { get; }
    public SandboxAuthority Foreign { get; }
    public ITrustAnchors Anchors { get; }
    public byte[] KnockSecret { get; }
    public IImSigner TokenAuthority { get; }
    public IImSigner DopplerIdentity { get; }
    public IImSigner GateKey { get; }
    public NoAudit Audit { get; } = new();
    public DopplerServer Doppler { get; }
    public ImCounters Counters { get; } = new();
    public HeartbeatBoard Board { get; } = new();
    public MilkyWayHealth Health { get; }
    public MilkyWayGate Gate { get; }
    public PeerWatch MilkyWaysWatchOnDoppler { get; }

    public static SimulatedMachine Good => new(SecureBoot: true, DiskEncrypted: true, AgentPackage);

    /// <summary>A machine with a TPM and a network stack, planned for <paramref name="role"/> unless <paramref name="planned"/> is false.</summary>
    public (DopplerAgent Agent, SimulatedDeviceHost Host) NewMachine(bool planned = true, EnrolledRole role = EnrolledRole.Laptop)
    {
        var tpm = new SimulatedTpm(Good);
        owned.Add(tpm);
        if (planned) Doppler.AddPlan(new EnrolmentPlan("plan-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, role, ["admin-a", "admin-b"]));
        var host = new SimulatedDeviceHost();
        return (new DopplerAgent(tpm, host, new DirectChannel(Doppler)), host);
    }

    /// <summary>A machine whose agent talks to Doppler only through Milky Way's enrolment door, as it does outside the sandbox.</summary>
    public (DopplerAgent Agent, SimulatedTpm Tpm, Func<DoorChannel> Door) NewMachineBehindTheDoor(bool planned = true, EnrolledRole role = EnrolledRole.Laptop)
    {
        var tpm = new SimulatedTpm(Good);
        owned.Add(tpm);
        if (planned) Doppler.AddPlan(new EnrolmentPlan("plan-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, role, ["admin-a", "admin-b"]));
        var door = new SwitchableChannel();
        var agent = new DopplerAgent(tpm, new SimulatedDeviceHost(), door);
        return (agent, tpm, () => door.Use(OpenDoor()));
    }

    /// <summary>A new gate connection, knocked and with the gate checked, ready for door frames.</summary>
    public DoorChannel OpenDoor() => new(new SimulatedLink(new SimulatedConnection(Gate.Open())), new ImClientSession(Anchors, Clock), KnockSecret);

    /// <summary>Connects a client with this key and token to the gate and runs the handshake.</summary>
    public (SimulatedConnection Connection, ImClientSession Client, bool Established) Connect(IImSigner deviceKey, byte[] token)
    {
        var connection = new SimulatedConnection(Gate.Open());
        var client = new ImClientSession(Anchors, deviceKey, token, Clock);
        var established = connection.Handshake(client, KnockSecret);
        return (connection, client, established);
    }

    /// <summary>Milky Way's signed report, from CM.MilkyWay's own <see cref="IHealthSource"/>.</summary>
    public SignedHealthReport MilkyWayReport() => Health.Report();

    public void Dispose()
    {
        foreach (var d in owned) d.Dispose();
        Authority.Dispose();
        Foreign.Dispose();
    }
}
