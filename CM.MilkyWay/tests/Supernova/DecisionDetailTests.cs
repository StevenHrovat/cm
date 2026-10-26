using System.Net.Security;
using Im;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Health;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Transport;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests.Supernova;

/// <summary>
/// Details of the gate's decision that the mutation run of Supernova level 3 (DMP chapter 06) found unpinned: each test here fails when
/// the line it names is changed. The reasons for the changes no test can see are in <c>docs/supernova/README.md</c>, "Mutation testing".
/// </summary>
[Trait("Category", "im")]
public sealed class DecisionDetailTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    static ImHeartbeat Beat(ulong sequence = 1) => new(sequence, ImHeartbeatStatus.Healthy, "0.1.0");

    [Fact]
    public void A_failure_inside_the_session_after_the_knock_answers_refused_and_counts_malformed()
    {
        using var world = new SandboxWorld(Start, door: new ScriptedDoor(_ => throw new InvalidOperationException("the door broke")));
        var connection = world.Connect();
        var client = new ImClientSession(world.Anchors, world.Clock);
        client.CheckGate(connection.Send(client.Knock(world.KnockSecret)).Frame!);

        var reply = connection.Send(client.Door("enrol me"u8));

        Assert.True(reply.Close);
        Assert.Equal(ImFrames.Refused(), reply.Frame);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Malformed));
        Assert.Equal(1, world.Counters.TotalRefused);
    }

    [Fact]
    public void An_abort_before_the_knock_is_quiet_and_after_the_knock_is_refused()
    {
        using var world = new SandboxWorld(Start);
        var before = world.Gate.Open();
        Assert.Equal(GateReply.Quiet, before.Abort(ImRefusal.Oversize));

        var after = world.Gate.Open();
        var client = world.EnrolDevice().NewClient();
        Assert.False(after.Receive(client.Knock(world.KnockSecret)).Close);
        Assert.Equal(GateReply.Refused, after.Abort(ImRefusal.Oversize));
        Assert.Equal(GateReply.Quiet, after.Abort(ImRefusal.Oversize)); // a closed session is not counted twice

        Assert.Equal(2, world.Counters.RefusedCount(ImRefusal.Oversize));
    }

    [Fact]
    public void A_knock_is_accepted_while_the_current_and_the_previous_secret_are_still_the_same()
    {
        // The first step of a rotation that was never finished: both entries hold the same secret. One match is enough.
        var secret = new byte[32];
        secret[0] = 7;
        using var world = new SandboxWorld(Start, new MilkyWaySettings { KnockSecrets = [secret, secret.ToArray()] });
        var connection = world.Connect();

        var accept = connection.Send(world.EnrolDevice().NewClient().Knock(secret));

        Assert.False(accept.Close);
        Assert.Equal(0, world.Counters.TotalRefused);
    }

    [Fact]
    public void A_response_whose_token_has_another_version_is_counted_as_unknown_version()
    {
        using var world = new SandboxWorld(Start);
        var device = world.EnrolDevice();
        var token = device.Token.ToArray();
        token[4 + 3] = 1; // the version byte after the body length and the magic "IMT"
        var connection = world.Connect();
        var client = new ImClientSession(world.Anchors, device.Key, token, world.Clock);
        var accept = connection.Send(client.Knock(world.KnockSecret));

        var reply = connection.Send(client.Respond(accept.Frame!));

        Assert.Equal(ImFrames.Refused(), reply.Frame);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.UnknownVersion));
        Assert.Equal(1, world.Counters.TotalRefused);
    }

    [Fact]
    public void A_session_alone_puts_its_token_in_the_health_report_for_Doppler()
    {
        using var world = new SandboxWorld(Start);
        var device = world.EnrolDevice();

        device.Handshake(); // no heartbeat: the session is what Doppler cross-checks (DMP chapter 05, "The contract with Doppler")

        Assert.Equal([Convert.ToHexStringLower(ImTokenCodec.Parse(device.Token).TokenId)], world.Health.Report().Report.AcceptedTokenIds);
    }

    [Fact]
    public async Task A_wrong_knock_over_TLS_gets_no_byte_and_the_gate_finishes_cleanly()
    {
        using var world = new SandboxWorld(Start);
        var device = world.EnrolDevice();
        var (end, served) = world.ConnectStream();
        await using (var tls = new SslStream(end, leaveInnerStreamOpen: false))
        {
            using var identity = ImTlsIdentity.Of(device.Key);
            await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, world.Anchors, world.Clock));
            await ImStreamFraming.WriteAsync(tls, device.NewClient().Knock(new byte[32]), CancellationToken.None);

            Assert.Equal(ImStreamOutcome.Closed, await ReadOutcomeAsync(tls, world.Settings.MaxFrameBytes));
        }

        await served; // completes, never faults
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.KnockRefused));
        Assert.Equal(1, world.Counters.TotalRefused);
    }

    [Fact]
    public void A_family_count_is_the_sum_of_its_reasons()
    {
        var counters = new ImCounters();
        counters.Refused(ImRefusal.BadMessageSignature);
        counters.Refused(ImRefusal.BadMessageSignature);
        counters.Refused(ImRefusal.BadTokenSignature);

        Assert.Equal(ImRefusal.BadMessageSignature.Family(), ImRefusal.BadTokenSignature.Family());
        Assert.Equal(3, counters.RefusedCount(ImRefusal.BadMessageSignature.Family()));
    }

    [Fact]
    public void The_snapshot_lists_accepted_then_refused_each_in_the_order_of_the_enum()
    {
        var counters = new ImCounters();
        counters.Refused(ImRefusal.Replayed);
        counters.Refused(ImRefusal.Oversize);
        counters.Accepted(ImAccepted.Heartbeat);
        counters.Accepted(ImAccepted.Session);

        (string, long)[] expected = [("accepted Session", 1), ("accepted Heartbeat", 1), ("refused Oversize", 1), ("refused Replayed", 1)];
        Assert.Equal(expected, counters.Snapshot());
    }

    [Fact]
    public void The_replay_memory_counts_what_it_holds_and_forgets_only_what_expired()
    {
        var memory = new ReplayMemory(capacity: 4);
        byte[] scope = [1, 2, 3, 4];
        Assert.Equal(ReplayOutcome.New, memory.TryRemember(scope, [1], expiresAt: 100, now: 0));
        Assert.Equal(ReplayOutcome.New, memory.TryRemember(scope, [2], expiresAt: 200, now: 0));
        Assert.Equal(2, memory.Count);

        Assert.Equal(ReplayOutcome.New, memory.TryRemember(scope, [3], expiresAt: 300, now: 150));

        Assert.Equal(2, memory.Count);
        Assert.Equal(ReplayOutcome.Seen, memory.TryRemember(scope, [2], expiresAt: 200, now: 150));
    }

    [Fact]
    public async Task The_gate_the_verifier_and_the_transport_refuse_a_missing_argument_at_once()
    {
        using var world = new SandboxWorld(Start);
        Assert.Throws<ArgumentNullException>(() => new MilkyWayGate(null!, world.GateKey, world.Anchors, world.Enrolment, world.Clock, world.Counters, world.Health));
        Assert.Throws<ArgumentNullException>(() => new ImVerifier(null!, world.Anchors, world.Enrolment, world.Clock, world.Counters));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Transport.ServeAsync(null!));
    }

    [Fact]
    public void A_door_answer_of_exactly_the_largest_size_goes_back_and_one_byte_more_is_refused()
    {
        var size = new MilkyWaySettings().MaxDoorBytes;
        using var exact = new SandboxWorld(Start, door: new ScriptedDoor(_ => new byte[size]));
        var (connection, client) = Accepted(exact);
        var reply = connection.Send(client.Door("enrol me"u8));
        Assert.False(reply.Close);
        Assert.Equal(size, ImClientSession.ReadDoorReply(reply.Frame).Length);

        using var over = new SandboxWorld(Start, door: new ScriptedDoor(_ => new byte[size + 1]));
        var (overConnection, overClient) = Accepted(over);
        Assert.True(overConnection.Send(overClient.Door("enrol me"u8)).Close);
        Assert.Equal(1, over.Counters.RefusedCount(ImRefusal.DoorRefused));
    }

    [Fact]
    public void A_response_whose_token_is_not_a_token_is_counted_as_malformed()
    {
        using var world = new SandboxWorld(Start);
        var device = world.EnrolDevice();
        var token = device.Token.ToArray();
        token[4] = (byte)'X'; // the first byte of the magic "IMT"
        var connection = world.Connect();
        var client = new ImClientSession(world.Anchors, device.Key, token, world.Clock);
        var accept = connection.Send(client.Knock(world.KnockSecret));

        Assert.Equal(ImFrames.Refused(), connection.Send(client.Respond(accept.Frame!)).Frame);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Malformed));
        Assert.Equal(1, world.Counters.TotalRefused);
    }

    static readonly byte[] Secret = Enumerable.Repeat((byte)7, 32).ToArray();

    public static TheoryData<string, MilkyWaySettings> Refused() => new()
    {
        { "knockSecrets needs", new MilkyWaySettings() },
        { "knockSecrets needs", new MilkyWaySettings { KnockSecrets = [Secret, Secret, Secret] } },
        { "shorter than 32", new MilkyWaySettings { KnockSecrets = [Secret, new byte[31]] } },
        { "time setting", new MilkyWaySettings { KnockSecrets = [Secret], ClockSkew = TimeSpan.Zero } },
        { "time setting", new MilkyWaySettings { KnockSecrets = [Secret], TokenMaxLifetime = TimeSpan.Zero } },
        { "time setting", new MilkyWaySettings { KnockSecrets = [Secret], HandshakeTimeout = TimeSpan.Zero } },
        { "time setting", new MilkyWaySettings { KnockSecrets = [Secret], IdleTimeout = TimeSpan.Zero } },
        { "limit is not positive", new MilkyWaySettings { KnockSecrets = [Secret], MaxFrameBytes = 0 } },
        { "limit is not positive", new MilkyWaySettings { KnockSecrets = [Secret], MaxPayloadBytes = 0 } },
        { "limit is not positive", new MilkyWaySettings { KnockSecrets = [Secret], ReplayCapacity = 0 } },
        { "limit is not positive", new MilkyWaySettings { KnockSecrets = [Secret], MaxConnections = 0 } },
        { "maxUncertifiedConnections", new MilkyWaySettings { KnockSecrets = [Secret], MaxUncertifiedConnections = 0 } },
        { "maxUncertifiedConnections", new MilkyWaySettings { KnockSecrets = [Secret], MaxConnections = 10, MaxUncertifiedConnections = 11 } },
        { "maxPayloadBytes must", new MilkyWaySettings { KnockSecrets = [Secret], MaxPayloadBytes = 65536 } },
        { "maxDoorBytes must", new MilkyWaySettings { KnockSecrets = [Secret], MaxDoorFrames = -1 } },
        { "maxDoorBytes must", new MilkyWaySettings { KnockSecrets = [Secret], MaxDoorBytes = 0 } },
        { "maxDoorBytes must", new MilkyWaySettings { KnockSecrets = [Secret], MaxFrameBytes = 1 << 20, MaxDoorBytes = ImLimits.DoorMax + 1 } },
        { "maxDoorBytes must", new MilkyWaySettings { KnockSecrets = [Secret], MaxDoorBytes = 65536 } },
        { "maxDoorBytes must", new MilkyWaySettings { KnockSecrets = [Secret], MaxFrameBytes = 8192, MaxDoorBytes = 8192 } },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Each_setting_that_cannot_run_a_gate_is_refused_with_its_own_reason(string reason, MilkyWaySettings settings) =>
        Assert.Contains(reason, Assert.Throws<InvalidOperationException>(settings.Validate).Message, StringComparison.Ordinal);

    [Fact]
    public void The_edges_of_the_settings_are_allowed()
    {
        new MilkyWaySettings { KnockSecrets = [Secret, Secret] }.Validate();
        new MilkyWaySettings { KnockSecrets = [Secret], MaxConnections = 10, MaxUncertifiedConnections = 10 }.Validate();
        new MilkyWaySettings { KnockSecrets = [Secret], MaxPayloadBytes = 65535, MaxDoorBytes = 1 }.Validate();
        new MilkyWaySettings { KnockSecrets = [Secret], MaxDoorFrames = 0 }.Validate();
        new MilkyWaySettings { KnockSecrets = [Secret], MaxFrameBytes = 1 << 20, MaxDoorBytes = ImLimits.DoorMax }.Validate();
    }

    [Fact]
    public void The_settings_page_marks_only_the_knock_secrets_as_secret_and_shows_units()
    {
        var settings = MilkyWaySettings.Describe();
        Assert.Equal(["milkyway.im.knockSecrets"], settings.Where(s => s.IsSecret).Select(s => s.Key));
        Assert.Equal("2 min", settings.Single(s => s.Key == "milkyway.im.clockSkew").Default);
        Assert.Equal("24 h", settings.Single(s => s.Key == "milkyway.im.tokenMaxLifetime").Default);
        Assert.Equal("10 s", settings.Single(s => s.Key == "milkyway.im.handshakeTimeout").Default);
    }

    [Fact]
    public void Every_refusal_reason_belongs_to_the_family_the_counters_report_it_under()
    {
        var expected = new Dictionary<ImRefusalFamily, ImRefusal[]>
        {
            [ImRefusalFamily.Malformed] = [ImRefusal.Oversize, ImRefusal.Malformed, ImRefusal.UnknownVersion],
            [ImRefusalFamily.Forged] = [ImRefusal.UntrustedTokenSigner, ImRefusal.BadTokenSignature, ImRefusal.BadMessageSignature, ImRefusal.SessionMismatch,
                ImRefusal.UntrustedDevice, ImRefusal.DeviceKeyMismatch, ImRefusal.BadResponseSignature, ImRefusal.TokenPolicy, ImRefusal.TransportMismatch, ImRefusal.TransportNoCertificate],
            [ImRefusalFamily.Expired] = [ImRefusal.CertificateExpired, ImRefusal.TransportCertificateExpired, ImRefusal.TokenNotYetValid, ImRefusal.TokenExpired],
            [ImRefusalFamily.Unenrolled] = [ImRefusal.NotEnrolled, ImRefusal.Revoked, ImRefusal.EnrolmentMismatch, ImRefusal.DoorClosed, ImRefusal.DoorRefused],
            [ImRefusalFamily.Replayed] = [ImRefusal.Replayed, ImRefusal.KnockReplayed],
            [ImRefusalFamily.Stale] = [ImRefusal.Stale, ImRefusal.KnockStale],
            [ImRefusalFamily.WrongPermission] = [ImRefusal.WrongPermission],
            [ImRefusalFamily.Unknown] = [ImRefusal.UnknownKind],
            [ImRefusalFamily.Quiet] = [ImRefusal.KnockRefused, ImRefusal.TransportRefused],
            [ImRefusalFamily.Protocol] = [ImRefusal.OutOfOrder, ImRefusal.HandshakeTimeout, ImRefusal.IdleTimeout],
            [ImRefusalFamily.Capacity] = [ImRefusal.ReplayCacheFull, ImRefusal.DoorLimit, ImRefusal.ConnectionLimit],
        };
        Assert.Equal(Enum.GetValues<ImRefusal>().Order(), expected.Values.SelectMany(r => r).Order()); // every reason listed once
        foreach (var (family, reasons) in expected)
            foreach (var reason in reasons) Assert.True(reason.Family() == family, $"{reason} is reported under {reason.Family()}, expected {family}");
    }

    [Fact]
    public void The_health_report_lists_token_ids_sorted_up_to_its_cap_and_counts_the_rest()
    {
        using var world = new SandboxWorld(Start);
        var health = new MilkyWayHealth(world.GateKey, new ImCounters(), world.Board, world.Clock, maxTokenIdsPerReport: 2);
        foreach (var id in new[] { "cc", "aa", "bb", "aa", "cc" }) health.TokenAccepted(id);

        var report = health.Report().Report;
        Assert.Equal(["aa", "cc"], report.AcceptedTokenIds);
        Assert.Equal(1, report.Counts["health.tokenids.dropped"]); // "bb" once; the repeats of listed ids are not dropped

        Assert.False(health.Report().Report.Counts.ContainsKey("health.tokenids.dropped"));
    }

    [Fact]
    public void A_message_with_a_renewed_token_inside_an_older_session_reports_the_new_token_id()
    {
        using var world = new SandboxWorld(Start);
        var device = world.EnrolDevice();
        var (connection, _) = device.Handshake();
        var renewed = world.IssueToken(device);
        world.Health.Report(); // starts a new report period: the session's token id is in the one before

        var envelope = ImEnvelopeCodec.Seal(ImKinds.Heartbeat, Beat().Encode(), renewed, device.Key, world.Clock.GetUtcNow(), world.Settings.MaxPayloadBytes);
        Assert.False(connection.Send(ImFrames.Message(envelope)).Close);

        Assert.Equal([Convert.ToHexStringLower(ImTokenCodec.Parse(renewed).TokenId)], world.Health.Report().Report.AcceptedTokenIds);
    }

    [Fact]
    public void The_board_lists_its_entries_by_subject()
    {
        using var world = new SandboxWorld(Start);
        var devices = Enumerable.Range(0, 3).Select(_ => world.EnrolDevice()).ToArray();
        foreach (var device in devices.Reverse())
        {
            var (connection, client) = device.Handshake();
            connection.Send(client.Heartbeat(device.NextHeartbeat()));
        }

        var subjects = world.Board.Entries.Select(e => e.Subject).ToArray();
        Assert.Equal(3, subjects.Length);
        Assert.Equal(subjects.Order(StringComparer.Ordinal), subjects);
    }

    static (SimulatedConnection Connection, ImClientSession Client) Accepted(SandboxWorld w)
    {
        var connection = w.Connect();
        var client = new ImClientSession(w.Anchors, w.Clock);
        client.CheckGate(connection.Send(client.Knock(w.KnockSecret)).Frame!);
        return (connection, client);
    }

    static async Task<ImStreamOutcome> ReadOutcomeAsync(Stream tls, int max)
    {
        try
        {
            return (await ImStreamFraming.ReadAsync(tls, max, CancellationToken.None)).Outcome;
        }
        catch (IOException)
        {
            return ImStreamOutcome.Closed;
        }
    }
}
