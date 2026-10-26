using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using CM.BlackHole.Doppler;
using CM.MilkyWay;
using CM.MilkyWay.Board;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Health;
using CM.MilkyWay.Transport;
using CM.MilkyWay.Verify;
using Im;
using Im.Trust;
using Im.Trust.Sandbox;

// cm-doppler-testhost --gate-host <name> --agent-config <path> [--agent-package <sha256>] [--listen <address>] [--port <port>]
//
// The test host of the agent's VM proof (lane L4b goal 3). It runs Milky Way's gate behind the TLS transport on one TCP port, with
// Doppler behind the gate's door and a sandbox CA, all in memory, and writes the agent.json the test package carries (the gate's
// name, the port, a fresh knock secret, the sandbox root). It never prints the knock secret. When it stops it forgets everything:
// a VM enrolled against it then runs "cm-doppler-agent forget" and is planned again. Commands on the console:
//   allow <sha256>                     allows an agent package (the hash package.ps1 prints; the package needs this host's agent.json first)
//   plan <endorsement key id> [role]   adds an enrolment plan (role Test unless named; two test approvers)
//   revoke <subject>                   revokes an enrolled machine; its next session is refused at the gate
//   status                             the register, the heartbeat board and the gate's counters
//   quit
var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("usage: cm-doppler-testhost --gate-host <name the VM uses> --agent-config <path> [--agent-package <sha256 of cm-doppler-agent.exe>] [--listen 127.0.0.1] [--port 5200]");
    return 2;
}

var time = TimeProvider.System;
var now = time.GetUtcNow();
using var authority = new SandboxAuthority("sandbox", now.AddDays(-1));
var anchors = authority.Anchors();
var knockSecret = RandomNumberGenerator.GetBytes(32);
var tokenAuthority = authority.Issue(TrustRole.ImTokenSigner, "doppler token authority 1", now.AddDays(-1), now.AddDays(30));
var dopplerIdentity = authority.Issue(TrustRole.DopplerServer, "doppler", now.AddDays(-1), now.AddDays(30));
var gateKey = authority.Issue(TrustRole.MilkyWayGate, "milky-way-gate", now.AddDays(-1), now.AddDays(30));
var audit = new ConsoleAudit();
var allowed = new SwapSet(options.AgentPackage);
var doppler = new DopplerServer(new DopplerSettings { AllowedAgentPackages = allowed },
    authority, tokenAuthority, dopplerIdentity, new ImTokenEncoder(), anchors, audit, time, MilkyWayHealth.ComponentName);
var counters = new ImCounters();
var board = new HeartbeatBoard();
var health = new MilkyWayHealth(gateKey, counters, board, time);
var gate = new MilkyWayGate(new MilkyWaySettings { KnockSecrets = [knockSecret] }, gateKey, anchors, doppler.Registry, time, counters, health, new DopplerDoor(doppler));
using var transport = new GateTransport(gate);

WriteAgentConfig(options, knockSecret, authority);
Console.WriteLine($"wrote {options.AgentConfig} (gate {options.GateHost}:{options.Port}; it holds this run's knock secret, give it to the test package only)");

var listener = new TcpListener(options.Listen, options.Port);
listener.Start();
Console.WriteLine($"listening on {options.Listen}:{options.Port}; commands: allow <sha256>, plan <ek id> [role], revoke <subject>, status, quit");
using var stop = new CancellationTokenSource();
var accepting = Task.Run(async () =>
{
    while (!stop.IsCancellationRequested)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { break; }
        _ = Task.Run(async () =>
        {
            using (client)
            {
                try { await transport.ServeAsync(client.GetStream(), stop.Token).ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
            }
        });
    }
});

while (Console.ReadLine() is { } line)
{
    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    switch (words)
    {
        case ["allow", var hash] when hash.Length == 64 && hash.All(Uri.IsHexDigit):
            allowed.Add(hash.ToLowerInvariant());
            Console.WriteLine($"allowed agent package {hash.ToLowerInvariant()}");
            break;
        case ["plan", var ek, .. var rest]:
            var role = rest is [var name] && Enum.TryParse<EnrolledRole>(name, ignoreCase: true, out var parsed) ? parsed : EnrolledRole.Test;
            Console.WriteLine(doppler.AddPlan(new EnrolmentPlan("test-" + ek[..Math.Min(8, ek.Length)], ek.ToLowerInvariant(), role, ["test-approver-1", "test-approver-2"]))
                ? $"planned {ek} as {role}" : "refused (the endorsement key id is 64 hex characters, as diagnose and status print it)");
            break;
        case ["revoke", var subject]:
            Console.WriteLine(doppler.Revoke(subject) ? $"revoked {subject}" : "no such enrolled subject");
            break;
        case ["status"]:
            foreach (var e in doppler.Registry.Entries)
                Console.WriteLine($"register  {e.Subject} {e.Role} enrolled {e.EnrolledAt.ToString("u", CultureInfo.InvariantCulture)}{(e.Revoked ? " REVOKED" : "")}");
            foreach (var b in board.Entries)
                Console.WriteLine($"heartbeat {b.Subject} seq {b.Sequence} {b.Status} v{b.AgentVersion} at {b.ReceivedAt.ToString("u", CultureInfo.InvariantCulture)} verified {b.Verified}");
            foreach (var hash in allowed) Console.WriteLine($"allowed   {hash}");
            foreach (var (what, count) in counters.Snapshot()) Console.WriteLine($"gate      {what} {count}");
            break;
        case ["quit"]:
            goto done;
        case []:
            break;
        default:
            Console.WriteLine("commands: allow <sha256>, plan <ek id> [role], revoke <subject>, status, quit");
            break;
    }
}
done:
stop.Cancel();
listener.Stop();
await accepting.ConfigureAwait(false);
return 0;

static void WriteAgentConfig(Options options, byte[] knockSecret, SandboxAuthority authority)
{
    var config = new
    {
        version = 1,
        gateHost = options.GateHost,
        gatePort = options.Port,
        knockSecret = Convert.ToBase64String(knockSecret),
        trustRoots = new[] { Convert.ToBase64String(authority.Root.RawData) },
    };
    var folder = Path.GetDirectoryName(Path.GetFullPath(options.AgentConfig));
    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
    File.WriteAllBytes(options.AgentConfig, JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions { WriteIndented = true }));
}

sealed record Options(IPAddress Listen, int Port, string GateHost, string? AgentPackage, string AgentConfig)
{
    public static Options? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) return null;
            values[args[i][2..]] = args[i + 1];
        }
        if (args.Length % 2 != 0) return null;
        if (!values.TryGetValue("gate-host", out var gateHost) || !values.TryGetValue("agent-config", out var config)) return null;
        var package = values.GetValueOrDefault("agent-package");
        if (package is not null && (package.Length != 64 || !package.All(Uri.IsHexDigit))) return null;
        var listen = values.TryGetValue("listen", out var l) ? IPAddress.Parse(l) : IPAddress.Loopback;
        var port = values.TryGetValue("port", out var p) ? int.Parse(p, CultureInfo.InvariantCulture) : 5200;
        return new Options(listen, port, gateHost, package?.ToLowerInvariant(), config);
    }
}

/// <summary>Doppler's token seam filled with Milky Way's codec, as in the sandbox pair (tools/sandbox-pair, PairWorld.cs).</summary>
sealed class ImTokenEncoder : ITokenEncoder
{
    public IssuedToken Encode(TokenClaims claims, IImSigner authority)
    {
        var encoded = ImTokenCodec.Issue(new ImTokenClaims(claims.Subject, claims.Role, claims.DeviceKeySpki, claims.Permissions, claims.IssuedAt, claims.Expires), authority, claims.IssuedAt);
        return new IssuedToken(Convert.ToHexStringLower(ImTokenCodec.Parse(encoded).TokenId), encoded, claims.Expires);
    }
}

/// <summary>Doppler's audit lines on the console (action, target, outcome; Doppler writes no secret into them).</summary>
sealed class ConsoleAudit : IDopplerAudit
{
    public void Write(AuditLine line) =>
        Console.WriteLine($"audit     {line.At.ToString("u", CultureInfo.InvariantCulture)} {line.Action} {line.Target} {line.Outcome}");
}

/// <summary>The allowed agent packages, which the console can add to while Doppler reads them on other threads.</summary>
sealed class SwapSet(string? first) : IReadOnlySet<string>
{
    ImmutableHashSet<string> set = first is null ? ImmutableHashSet<string>.Empty : [first];

    ImmutableHashSet<string> Current => Volatile.Read(ref set);

    public void Add(string item) => ImmutableInterlocked.Update(ref set, s => s.Add(item));

    public int Count => Current.Count;
    public bool Contains(string item) => Current.Contains(item);
    public bool IsProperSubsetOf(IEnumerable<string> other) => Current.IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<string> other) => Current.IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<string> other) => Current.IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<string> other) => Current.IsSupersetOf(other);
    public bool Overlaps(IEnumerable<string> other) => Current.Overlaps(other);
    public bool SetEquals(IEnumerable<string> other) => Current.SetEquals(other);
    public IEnumerator<string> GetEnumerator() => Current.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
