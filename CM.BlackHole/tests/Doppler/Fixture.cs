using System.Security.Cryptography;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;
using Im.Trust.Sandbox;

namespace CM.BlackHole.Tests.Doppler;

/// <summary>A clock that moves only when the test says so (DMP chapter 10).</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>Stands in for Milky Way's token codec: a token id and the claims' subject, signed by the authority. Enough for Doppler's own tests; the pair test uses the real codec.</summary>
public sealed class FakeTokenEncoder : ITokenEncoder
{
    int next;

    public List<TokenClaims> Issued { get; } = [];

    public IssuedToken Encode(TokenClaims claims, IImSigner authority)
    {
        Issued.Add(claims);
        var id = "tok-" + Interlocked.Increment(ref next).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        return new IssuedToken(id, authority.Sign(System.Text.Encoding.UTF8.GetBytes(id + "|" + claims.Subject)), claims.Expires);
    }
}

public sealed class ListAudit : IDopplerAudit
{
    public List<AuditLine> Lines { get; } = [];

    public void Write(AuditLine line)
    {
        lock (Lines) Lines.Add(line);
    }
}

/// <summary>One sandbox: a two-tier authority, Doppler with its two keys, and helpers to make machines.</summary>
public sealed class DopplerWorld : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    public const string AgentPackage = "0000000000000000000000000000000000000000000000000000000000000001";

    readonly List<IDisposable> owned = [];

    public DopplerWorld(DopplerSettings? settings = null, IDopplerStore? store = null)
    {
        Clock = new ManualClock(Start);
        Authority = new SandboxAuthority("sandbox", Start.AddMonths(-10));
        TokenSigner = Authority.Issue(TrustRole.ImTokenSigner, "doppler token authority 1", Start.AddDays(-1), Start.AddDays(89));
        Identity = Authority.Issue(TrustRole.DopplerServer, "doppler", Start.AddDays(-1), Start.AddDays(364));
        Settings = settings ?? new DopplerSettings { AllowedAgentPackages = new HashSet<string>([AgentPackage], StringComparer.Ordinal) };
        Server = new DopplerServer(Settings, Authority, TokenSigner, Identity, Encoder, Authority.Anchors(), Audit, Clock, "milkyway", store);
    }

    public ManualClock Clock { get; }
    public SandboxAuthority Authority { get; }
    public IImSigner TokenSigner { get; }
    public IImSigner Identity { get; }
    public DopplerSettings Settings { get; }
    public FakeTokenEncoder Encoder { get; } = new();
    public ListAudit Audit { get; } = new();
    public DopplerServer Server { get; }

    public static SimulatedMachine Good => new(SecureBoot: true, DiskEncrypted: true, AgentPackage);

    public SimulatedTpm NewTpm(SimulatedMachine? machine = null, ushort attestationHash = TpmAlg.Sha256)
    {
        var tpm = new SimulatedTpm(machine ?? Good, attestationHash);
        owned.Add(tpm);
        return tpm;
    }

    public (DopplerAgent Agent, SimulatedTpm Tpm, SimulatedDeviceHost Host) NewMachine(bool planned = true, EnrolledRole role = EnrolledRole.Laptop, SimulatedMachine? machine = null)
    {
        var tpm = NewTpm(machine);
        var host = new SimulatedDeviceHost();
        if (planned) Server.AddPlan(new EnrolmentPlan("plan-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, role, ["admin-a", "admin-b"]));
        return (new DopplerAgent(tpm, host, new DirectChannel(Server)), tpm, host);
    }

    public static byte[] RandomBytes(int n) => RandomNumberGenerator.GetBytes(n);

    public void Dispose()
    {
        foreach (var d in owned) d.Dispose();
        Authority.Dispose();
    }
}
