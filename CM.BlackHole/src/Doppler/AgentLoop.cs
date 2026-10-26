using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Im;
using Im.Trust;

namespace CM.BlackHole.Doppler;

/// <summary>Every value an admin could tune in the agent (DMP chapter 10). Defaults here; on a machine they come from the agent's config file.</summary>
public sealed record AgentOptions
{
    /// <summary>How often an enrolled agent sends its heartbeat. Default 1 minute.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The first wait after a failed step (gate not reachable, enrolment refused). Doubles on each failure in a row. Default 5 minutes.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The longest wait between attempts. Default 1 hour.</summary>
    public TimeSpan MaxRetryInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>A new token is fetched when the current one expires within this. Default 5 minutes.</summary>
    public TimeSpan TokenRenewBefore { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The version the heartbeat carries.</summary>
    public string AgentVersion { get; init; } = "0.1.0";
}

/// <summary>
/// Opens connections to Milky Way's gate. Each call is one new connection; each throws <see cref="ImClientException"/>,
/// <see cref="IOException"/> or <see cref="SocketException"/> when the gate cannot be reached, is not genuine or refuses.
/// </summary>
public interface IGateConnector
{
    /// <summary>A connection without a Device certificate, knocked and with the gate checked, for the enrolment door (enrolment and tokens).</summary>
    DoorChannel OpenDoor();

    /// <summary>A connection with the device's certificate and token, handshake done (established), for messages.</summary>
    IGateSession OpenSession(IImSigner device, byte[] token);
}

/// <summary>An established IM session at the gate.</summary>
public interface IGateSession : IDisposable
{
    /// <summary>Sends the heartbeat and waits for the gate's ack; throws <see cref="ImClientException"/> when the gate refuses it.</summary>
    void Heartbeat(ImHeartbeat heartbeat);
}

/// <summary>What the agent keeps across a restart: its subject, its Device certificate and chain (public), the last heartbeat sequence. No key, no token.</summary>
public sealed record AgentRecord(string Subject, byte[] Certificate, IReadOnlyList<byte[]> Chain, ulong Sequence);

public interface IAgentStore
{
    AgentRecord? Load();

    void Save(AgentRecord record);
}

/// <summary>The agent's record in one JSON file in its data folder (the installer limits the folder to LocalSystem and administrators).</summary>
public sealed class FileAgentStore(string path) : IAgentStore
{
    sealed record Dto(int Version, string Subject, string Certificate, string[] Chain, ulong Sequence);

    public AgentRecord? Load()
    {
        if (!File.Exists(path)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllBytes(path));
            if (dto is null || dto.Version != 1) return null;
            return new AgentRecord(dto.Subject, Convert.FromBase64String(dto.Certificate), [.. dto.Chain.Select(Convert.FromBase64String)], dto.Sequence);
        }
        // A null field throws ArgumentNullException (GRB-0003 F6); every unreadable file means "not enrolled".
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException) { return null; }
    }

    public void Save(AgentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var dto = new Dto(1, record.Subject, Convert.ToBase64String(record.Certificate), [.. record.Chain.Select(Convert.ToBase64String)], record.Sequence);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>What one step of the agent did. Codes only; the service log writes these.</summary>
public enum AgentOutcome
{
    None,
    PortConflict,
    Enrolled,
    EnrolRefused,
    TokenRefused,
    SessionRefused,
    HeartbeatSent,
    HeartbeatRefused,
    GateUnreachable,
}

/// <summary>
/// The agent service's work (lane L4b goal 3), without the service around it so it runs the same in tests: take port 138, find the
/// enrolment it kept or enrol through the door, renew the token through the door when due, then per heartbeat open a session with the
/// Device certificate and send the heartbeat. <see cref="Step"/> does one round and says how long to wait before the next; failures
/// back off, doubling up to <see cref="AgentOptions.MaxRetryInterval"/>.
/// </summary>
public sealed class AgentLoop
{
    readonly IGateConnector gate;
    readonly IAgentStore store;
    readonly TimeProvider time;
    readonly AgentOptions options;
    readonly ChannelSlot slot = new();
    TimeSpan backoff;
    ulong sequence;
    AgentRecord? record;

    /// <param name="answers">The agent's trust anchors (the same as its gate connector's): Doppler's enrolment answer and a kept record are believed only
    /// when the Device certificate chains to them (GRB-0003 F3).</param>
    public AgentLoop(IDeviceTpm tpm, IDeviceHost host, IGateConnector gate, IAgentStore store, ITrustAnchors answers, TimeProvider time, AgentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tpm);
        ArgumentNullException.ThrowIfNull(host);
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.options = options ?? new AgentOptions();
        ArgumentNullException.ThrowIfNull(answers);
        Agent = new DopplerAgent(tpm, host, slot, answers, time);
    }

    public DopplerAgent Agent { get; }

    public AgentOutcome LastOutcome { get; private set; }

    /// <summary>The last heartbeat sequence sent (kept across restarts).</summary>
    public ulong Sequence => sequence;

    /// <summary>One round. Returns the wait before the next round.</summary>
    public TimeSpan Step()
    {
        if (Agent.State is AgentState.Stopped or AgentState.PortConflict)
        {
            if (Agent.Start() == AgentState.PortConflict) return Fail(AgentOutcome.PortConflict);
            Restore();
        }
        try
        {
            if (Agent.State != AgentState.Enrolled)
            {
                using (var door = gate.OpenDoor())
                {
                    slot.Use(door);
                    if (!Agent.Enrol()) return Fail(AgentOutcome.EnrolRefused);
                }
                record = new AgentRecord(Agent.Subject!, Agent.DeviceSigner!.Certificate.RawData, [.. Agent.DeviceSigner.Chain.Select(c => c.RawData)], sequence);
                store.Save(record);
                return Done(AgentOutcome.Enrolled, TimeSpan.Zero);
            }

            if (Agent.Token is null || Agent.Token.Expires - options.TokenRenewBefore <= time.GetUtcNow())
            {
                using var door = gate.OpenDoor();
                slot.Use(door);
                if (!Agent.RefreshToken()) return Fail(AgentOutcome.TokenRefused);
            }

            IGateSession session;
            try { session = gate.OpenSession(Agent.DeviceSigner!, Agent.Token!.Encoded); }
            catch (ImClientException) { return Fail(AgentOutcome.SessionRefused); }
            using (session)
            {
                try { session.Heartbeat(new ImHeartbeat(sequence + 1, ImHeartbeatStatus.Healthy, options.AgentVersion)); }
                catch (ImClientException) { return Fail(AgentOutcome.HeartbeatRefused); }
            }
            sequence++;
            if (record is not null) store.Save(record = record with { Sequence = sequence });
            return Done(AgentOutcome.HeartbeatSent, options.HeartbeatInterval);
        }
        catch (Exception e) when (e is ImClientException or ImFormatException or IOException or SocketException)
        {
            return Fail(AgentOutcome.GateUnreachable);
        }
    }

    /// <summary>Takes the kept record back. One that does not load, names no subject or binds to no present key is treated as absent (GRB-0003 F6).</summary>
    void Restore()
    {
        record = store.Load();
        if (record is null) return;
        if (string.IsNullOrWhiteSpace(record.Subject) || record.Certificate is null || record.Chain is null || record.Chain.Any(c => c is null))
        {
            record = null;
            return;
        }
        X509Certificate2 certificate;
        List<X509Certificate2> chain;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(record.Certificate);
            chain = [.. record.Chain.Select(c => X509CertificateLoader.LoadCertificate(c))];
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            record = null;
            return;
        }
        if (!Agent.Restore(record.Subject, certificate, chain))
        {
            record = null;
            return;
        }
        sequence = record.Sequence;
    }

    TimeSpan Done(AgentOutcome outcome, TimeSpan next)
    {
        LastOutcome = outcome;
        backoff = TimeSpan.Zero;
        return next;
    }

    TimeSpan Fail(AgentOutcome outcome)
    {
        LastOutcome = outcome;
        backoff = backoff == TimeSpan.Zero ? options.RetryInterval : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxRetryInterval.Ticks));
        return backoff;
    }

    /// <summary>The agent's channel to Doppler: each round puts the door of its fresh connection here.</summary>
    sealed class ChannelSlot : IDopplerChannel
    {
        DoorChannel? current;

        public void Use(DoorChannel channel) => current = channel;

        DoorChannel Current => current ?? throw new InvalidOperationException("no door connection");

        public byte[]? Challenge() => Current.Challenge();

        public BeginResult BeginEnrolment(EnrolBegin request) => Current.BeginEnrolment(request);

        public EnrolmentResult FinishEnrolment(EnrolFinish request) => Current.FinishEnrolment(request);

        public TokenResult IssueToken(TokenRequest request) => Current.IssueToken(request);
    }
}
