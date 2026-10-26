using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using CM.BlackHole.Agent;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Tpm;
using CM.BlackHole.Doppler.Windows;
using Im;
using Im.Trust;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: SupportedOSPlatform("windows")]

// cm-doppler-agent                 run as the Windows service (the installer registers it; LocalSystem)
// cm-doppler-agent run             the same loop in a console window, for a test by hand
// cm-doppler-agent diagnose        every enrolment step against this machine's TPM with keys of its own; prints the endorsement key id
// cm-doppler-agent status          the endorsement key id and the enrolment the agent keeps; no secret
// cm-doppler-agent forget          deletes the agent's TPM keys and the diagnose keys (the uninstaller runs it)
switch (args)
{
    case ["forget"]:
        {
            var agent = PcpTpm.DeleteKeys();
            var diagnose = PcpTpm.DeleteKeys("cm-doppler-diagnose");
            Console.WriteLine($"deleted: attestation key {agent.AttestationKey}, device key {agent.DeviceKey}, diagnose keys {diagnose.AttestationKey || diagnose.DeviceKey}");
            return 0;
        }
    case ["diagnose"]:
        {
            using var tpm = new PcpTpm(WindowsReport.Read, "cm-doppler-diagnose");
            return TpmDiagnosis.Run(tpm, Console.WriteLine) ? 0 : 1;
        }
    case ["status"]:
        {
            using var tpm = new PcpTpm(WindowsReport.Read);
            Console.WriteLine($"endorsement key id {tpm.EndorsementKeyId}");
            Console.WriteLine($"agent package {WindowsReport.AgentPackageSha256}");
            var record = new FileAgentStore(Path.Combine(AgentConfig.DataFolder, "state.json")).Load();
            Console.WriteLine(record is null ? "not enrolled" : $"enrolled as {record.Subject}, device key {(tpm.HasDeviceKey ? "present" : "missing")}, last heartbeat {record.Sequence}");
            return 0;
        }
    case [] or ["run"]:
        break;
    default:
        Console.Error.WriteLine("usage: cm-doppler-agent [run | diagnose | status | forget]");
        return 2;
}

var builder = Host.CreateApplicationBuilder();
builder.Services.AddWindowsService(o => o.ServiceName = AgentService.ServiceName);
// The event log provider writes warnings and up by default; the agent's outcome lines are information, and there are few (on change only).
builder.Logging.AddFilter<Microsoft.Extensions.Logging.EventLog.EventLogLoggerProvider>(level => level >= LogLevel.Information);
builder.Services.AddHostedService<AgentService>();
await builder.Build().RunAsync();
return 0;

/// <summary>
/// The agent service: builds the loop from the config file, the Windows TPM and the TLS connector, then runs <see cref="AgentLoop.Step"/>
/// and waits as long as it says. It logs outcomes and refusal codes only, never a key, a token, a certificate or the knock secret.
/// </summary>
sealed class AgentService(ILogger<AgentService> log) : BackgroundService
{
    public const string ServiceName = "CmDopplerAgent";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var folder = AgentConfig.DataFolder;
        var config = AgentConfig.Load(Path.Combine(folder, "agent.json"));
        var time = TimeProvider.System;
        var anchors = new TrustAnchors(config.TrustRoots, new NothingRevoked());
        using var tpm = new PcpTpm(WindowsReport.Read);
        using var host = new WindowsDeviceHost();
        var connector = new TlsGateConnector(() => Connect(config), anchors, config.KnockSecret, time, signer => Identity(tpm, signer));
        var loop = new AgentLoop(tpm, host, connector, new FileAgentStore(Path.Combine(folder, "state.json")), anchors, time, config.Options);
        log.LogInformation("doppler agent started, endorsement key id {EndorsementKeyId}", tpm.EndorsementKeyId);

        string? last = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try { wait = loop.Step(); }
            catch (Exception e) when (e is TpmCommandException or TpmFormatException or PcpException or CryptographicException or InvalidOperationException)
            {
                // The exception type and the TPM's or provider's code are logged; the message may carry bytes and is not.
                log.LogError("doppler agent step failed: {Type} {Code}", e.GetType().Name, e is PcpException p ? $"0x{p.Status:X8}" : e is TpmCommandException t ? $"0x{t.ResponseCode:X}" : "");
                wait = config.Options.RetryInterval;
            }
            // One line per change of outcome, not per heartbeat (a heartbeat a minute would fill the event log).
            var now = $"{loop.LastOutcome} {loop.Agent.LastRefusal} {loop.Agent.State}";
            if (now != last)
                log.LogInformation("doppler agent: {Outcome} (refusal {Refusal}, state {State}, heartbeat {Sequence}), next in {Wait}", loop.LastOutcome, loop.Agent.LastRefusal, loop.Agent.State, loop.Sequence, wait);
            last = now;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, time, stoppingToken).ConfigureAwait(false);
        }
    }

    static Stream Connect(AgentConfig config)
    {
        var client = new TcpClient();
        try
        {
            client.Connect(config.GateHost, config.GatePort);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    static ImTlsIdentity Identity(PcpTpm tpm, IImSigner signer) => ImTlsIdentity.Bound(tpm.TlsCertificate(signer.Certificate), signer.Chain);

    /// <summary>The device checks the gate's chain; revocation of the gate certificate is Milky Way's and Doppler's to watch, not the device's.</summary>
    sealed class NothingRevoked : IRevocationList
    {
        public bool IsRevoked(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) => false;
    }
}
