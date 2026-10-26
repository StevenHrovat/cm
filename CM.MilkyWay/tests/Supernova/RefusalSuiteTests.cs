using System.Net.Security;
using Im;
using Im.Trust;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests.Supernova;

/// <summary>
/// The refusal suite of Supernova level 3 (DMP chapter 06): every refusal reason the gate and the verifier know has a scenario that
/// produces it, and only it is counted for that scenario. A reason added to <see cref="ImRefusal"/> without a scenario here fails
/// <see cref="Every_refusal_reason_has_a_scenario"/>, so the suite cannot fall behind the code.
/// </summary>
[Trait("Category", "im")]
public sealed class RefusalSuiteTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    static ImHeartbeat Beat(ulong sequence = 1) => new(sequence, ImHeartbeatStatus.Healthy, "0.1.0");

    sealed record Scenario(Func<SandboxWorld, Task> Run, MilkyWaySettings? Settings = null, IImDoor? Door = null);

    static Scenario Sync(Action<SandboxWorld> run, MilkyWaySettings? settings = null, IImDoor? door = null) =>
        new(w => { run(w); return Task.CompletedTask; }, settings, door);

    static ReadOnlyMemory<byte> Envelope(SandboxWorld w, SimulatedDevice d, byte[]? token = null, IImSigner? key = null, string kind = ImKinds.Heartbeat,
        byte[]? payload = null, DateTimeOffset? sentAt = null) =>
        ImEnvelopeCodec.Seal(kind, payload ?? Beat().Encode(), token ?? d.Token, key ?? d.Key, sentAt ?? w.Clock.GetUtcNow(), w.Settings.MaxPayloadBytes);

    static void Verify(SandboxWorld w, ReadOnlyMemory<byte> envelope, string? session = null) => w.Gate.Verifier.Verify(envelope, session);

    static void Handshake(SimulatedDevice d) => d.Handshake();

    /// <summary>Knock, accept, then a response the test may change before it is sent.</summary>
    static void Respond(SandboxWorld w, SimulatedDevice d, Func<byte[], byte[]> change)
    {
        var connection = w.Connect();
        var client = d.NewClient();
        var accept = connection.Send(client.Knock(w.KnockSecret));
        connection.Send(change(client.Respond(accept.Frame!)));
    }

    /// <summary>Knock and accept as a machine without a certificate or a token, then send the given door payloads.</summary>
    static void ThroughTheDoor(SandboxWorld w, params string[] payloads)
    {
        var connection = w.Connect();
        var client = new ImClientSession(w.Anchors, w.Clock);
        client.CheckGate(connection.Send(client.Knock(w.KnockSecret)).Frame!);
        foreach (var payload in payloads)
            if (connection.Send(client.Door(System.Text.Encoding.ASCII.GetBytes(payload))).Close) return;
    }

    static async Task TlsThen(SandboxWorld w, SimulatedDevice? shown, Func<SslStream, Task> then)
    {
        var (end, served) = w.ConnectStream();
        using var identity = shown is null ? null : ImTlsIdentity.Of(shown.Key);
        await using (var tls = new SslStream(end, leaveInnerStreamOpen: false))
        {
            await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, w.Anchors, w.Clock));
            await then(tls);
        }
        await served;
    }

    static async Task TlsFailing(SandboxWorld w, SimulatedDevice d)
    {
        try { await d.ConnectTlsAsync(); }
        catch (ImClientException) { }
    }

    static readonly IReadOnlyDictionary<ImRefusal, Scenario> Scenarios = new Dictionary<ImRefusal, Scenario>
    {
        // ---- verify rules 1 to 16 ----
        [ImRefusal.Oversize] = Sync(w => Verify(w, new byte[w.Settings.MaxFrameBytes + 1])),
        [ImRefusal.Malformed] = Sync(w => Verify(w, "not an envelope"u8.ToArray())),
        [ImRefusal.UnknownVersion] = Sync(w =>
        {
            var bytes = Envelope(w, w.EnrolDevice()).ToArray();
            bytes[4 + 3] = 1; // the version byte after the body length and the magic "IMM"
            Verify(w, bytes);
        }),
        [ImRefusal.UntrustedTokenSigner] = Sync(w =>
        {
            var d = w.EnrolDevice();
            var foreign = w.Foreign.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Start.AddDays(-1), Start.AddDays(30));
            Verify(w, Envelope(w, d, w.IssueToken(d, signer: foreign)));
        }),
        [ImRefusal.CertificateExpired] = Sync(w =>
        {
            var d = w.EnrolDevice();
            var expired = w.Authority.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Start.AddDays(-9), Start.AddDays(-1));
            Verify(w, Envelope(w, d, w.IssueToken(d, signer: expired)));
        }),
        [ImRefusal.BadTokenSignature] = Sync(w =>
        {
            var d = w.EnrolDevice();
            var token = d.Token.ToArray();
            token[^64] ^= 0x01; // the first byte of r in the token signature
            Verify(w, Envelope(w, d, token));
        }),
        [ImRefusal.TokenPolicy] = Sync(w =>
        {
            var d = w.EnrolDevice();
            Verify(w, Envelope(w, d, w.IssueToken(d, notBefore: Start.AddMinutes(-1), expiresAt: Start.AddDays(3))));
        }),
        [ImRefusal.TokenNotYetValid] = Sync(w =>
        {
            var d = w.EnrolDevice();
            Verify(w, Envelope(w, d, w.IssueToken(d, notBefore: Start.AddHours(1), expiresAt: Start.AddHours(2))));
        }),
        [ImRefusal.TokenExpired] = Sync(w =>
        {
            var d = w.EnrolDevice();
            Verify(w, Envelope(w, d, w.IssueToken(d, notBefore: Start.AddHours(-3), expiresAt: Start.AddHours(-1))));
        }),
        [ImRefusal.NotEnrolled] = Sync(w => Verify(w, Envelope(w, w.UnenrolledDevice()))),
        [ImRefusal.Revoked] = Sync(w =>
        {
            var d = w.EnrolDevice();
            w.Enrolment.Revoke(d.Subject);
            Verify(w, Envelope(w, d));
        }),
        [ImRefusal.EnrolmentMismatch] = Sync(w =>
        {
            var d = w.EnrolDevice();
            w.Enrolment.Put(new EnrolmentRecord(d.Subject, EnrolledRole.Build, d.Key.KeyId, false));
            Verify(w, Envelope(w, d));
        }),
        [ImRefusal.SessionMismatch] = Sync(w => Verify(w, Envelope(w, w.EnrolDevice()), session: "d-another-device")),
        [ImRefusal.BadMessageSignature] = Sync(w =>
        {
            var d = w.EnrolDevice();
            var other = w.Authority.Issue(TrustRole.Device, d.Subject, Start.AddDays(-1), Start.AddDays(30));
            Verify(w, Envelope(w, d, key: other));
        }),
        [ImRefusal.Stale] = Sync(w => Verify(w, Envelope(w, w.EnrolDevice(), sentAt: Start.AddMinutes(-10)))),
        [ImRefusal.UnknownKind] = Sync(w =>
        {
            var d = w.EnrolDevice();
            Verify(w, Envelope(w, d, w.IssueToken(d, permissions: [.. SandboxWorld.DefaultPermissions, "im.reboot"]), kind: "im.reboot", payload: [1]));
        }),
        [ImRefusal.WrongPermission] = Sync(w =>
        {
            var d = w.EnrolDevice();
            Verify(w, Envelope(w, d, w.IssueToken(d, permissions: [ImKinds.SessionPermission])));
        }),
        [ImRefusal.Replayed] = Sync(w =>
        {
            var envelope = Envelope(w, w.EnrolDevice());
            Verify(w, envelope);
            Verify(w, envelope);
        }),
        [ImRefusal.ReplayCacheFull] = Sync(w =>
        {
            var d = w.EnrolDevice();
            Verify(w, Envelope(w, d));
            Verify(w, Envelope(w, d));
        }, new MilkyWaySettings { ReplayCapacity = 1 }),

        // ---- the handshake ----
        [ImRefusal.KnockRefused] = Sync(w => w.Connect().Send(w.EnrolDevice().NewClient().Knock(new byte[32]))),
        [ImRefusal.KnockStale] = Sync(w => w.Connect().Send(ImFrames.Knock(w.KnockSecret, Start.AddMinutes(-10)))),
        [ImRefusal.KnockReplayed] = Sync(w =>
        {
            var knock = w.EnrolDevice().NewClient().Knock(w.KnockSecret);
            w.Connect().Send(knock);
            w.Connect().Send(knock);
        }),
        [ImRefusal.UntrustedDevice] = Sync(w =>
        {
            var d = w.EnrolDevice();
            d.Key = w.Foreign.Issue(TrustRole.Device, d.Subject, Start.AddDays(-1), Start.AddDays(30));
            Handshake(d);
        }),
        [ImRefusal.DeviceKeyMismatch] = Sync(w =>
        {
            var d = w.EnrolDevice();
            d.Key = w.Authority.Issue(TrustRole.Device, d.Subject, Start.AddDays(-1), Start.AddDays(30)); // the token names the old key
            Handshake(d);
        }),
        [ImRefusal.BadResponseSignature] = Sync(w => Respond(w, w.EnrolDevice(), response =>
        {
            response[^64] ^= 0x01; // the first byte of r in the response signature
            return response;
        })),
        [ImRefusal.OutOfOrder] = Sync(w =>
        {
            var d = w.EnrolDevice();
            var connection = w.Connect();
            connection.Send(d.NewClient().Knock(w.KnockSecret));
            connection.Send(ImFrames.Message(Envelope(w, d).ToArray()));
        }),
        [ImRefusal.HandshakeTimeout] = Sync(w => Respond(w, w.EnrolDevice(), response =>
        {
            w.Clock.Advance(w.Settings.HandshakeTimeout + TimeSpan.FromSeconds(1));
            return response;
        })),

        // ---- the transport ----
        [ImRefusal.TransportRefused] = new(async w =>
        {
            var (end, served) = w.ConnectStream();
            await end.WriteAsync("not TLS at all"u8.ToArray());
            await served;
            await end.DisposeAsync();
        }),
        [ImRefusal.TransportMismatch] = new(async w =>
        {
            var shown = w.EnrolDevice();
            var speaking = w.EnrolDevice();
            await TlsThen(w, shown, async tls =>
            {
                var client = speaking.NewClient();
                await ImStreamFraming.WriteAsync(tls, client.Knock(w.KnockSecret), CancellationToken.None);
                var accept = await ImStreamFraming.ReadAsync(tls, w.Settings.MaxFrameBytes, CancellationToken.None);
                await ImStreamFraming.WriteAsync(tls, client.Respond(accept.Frame!), CancellationToken.None);
                await ImStreamFraming.ReadAsync(tls, w.Settings.MaxFrameBytes, CancellationToken.None);
            });
        }),
        [ImRefusal.TransportCertificateExpired] = new(async w =>
        {
            var d = w.EnrolDevice();
            d.Key = w.Authority.Issue(TrustRole.Device, d.Subject, Start.AddDays(-30), Start.AddDays(-1));
            await TlsFailing(w, d);
        }),
        [ImRefusal.ConnectionLimit] = new(async w =>
        {
            var (held, heldServed) = w.ConnectStream();
            var (extra, extraServed) = w.ConnectStream();
            await extraServed;
            w.Clock.Advance(w.Settings.HandshakeTimeout + TimeSpan.FromSeconds(1)); // frees the held one (counted as HandshakeTimeout)
            await heldServed;
            await held.DisposeAsync();
            await extra.DisposeAsync();
        }, new MilkyWaySettings { MaxConnections = 1, MaxUncertifiedConnections = 1 }),
        [ImRefusal.IdleTimeout] = new(async w =>
        {
            var (connection, served) = await w.EnrolDevice().ConnectTlsAsync();
            w.Clock.Advance(w.Settings.IdleTimeout + TimeSpan.FromSeconds(1));
            await served;
            await connection.DisposeAsync();
        }),
        [ImRefusal.TransportNoCertificate] = new(async w =>
        {
            var d = w.EnrolDevice();
            await TlsThen(w, null, async tls =>
            {
                var client = d.NewClient();
                await ImStreamFraming.WriteAsync(tls, client.Knock(w.KnockSecret), CancellationToken.None);
                var accept = await ImStreamFraming.ReadAsync(tls, w.Settings.MaxFrameBytes, CancellationToken.None);
                await ImStreamFraming.WriteAsync(tls, client.Respond(accept.Frame!), CancellationToken.None);
                await ImStreamFraming.ReadAsync(tls, w.Settings.MaxFrameBytes, CancellationToken.None);
            });
        }),
        // ---- the enrolment door (DMP chapter 04, "The enrolment door") ----
        [ImRefusal.DoorClosed] = Sync(w => ThroughTheDoor(w, "enrol me")),
        [ImRefusal.DoorRefused] = Sync(w => ThroughTheDoor(w, "enrol me"), door: new ScriptedDoor(_ => null)),
        [ImRefusal.DoorLimit] = Sync(w => ThroughTheDoor(w, "1", "2"), new MilkyWaySettings { MaxDoorFrames = 1 }, new ScriptedDoor(_ => "ok"u8.ToArray())),
    };

    /// <summary>Reasons a scenario may count on its way to its own reason (the setup of the scenario, not a second refusal of the same input).</summary>
    static readonly IReadOnlyDictionary<ImRefusal, ImRefusal[]> AlsoCounted = new Dictionary<ImRefusal, ImRefusal[]>
    {
        [ImRefusal.ConnectionLimit] = [ImRefusal.HandshakeTimeout],
    };

    public static TheoryData<ImRefusal> AllReasons() => new(Enum.GetValues<ImRefusal>());

    [Fact]
    public void Every_refusal_reason_has_a_scenario()
    {
        var missing = Enum.GetValues<ImRefusal>().Where(r => !Scenarios.ContainsKey(r)).ToArray();
        Assert.True(missing.Length == 0, "refusal reasons without a scenario in the refusal suite: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(AllReasons))]
    public async Task Each_reason_is_produced_by_its_scenario_and_counted_once(ImRefusal reason)
    {
        Assert.True(Scenarios.TryGetValue(reason, out var scenario), $"no scenario for {reason}");
        using var world = new SandboxWorld(Start, scenario.Settings, scenario.Door);

        await scenario.Run(world);

        Assert.Equal(1, world.Counters.RefusedCount(reason));
        var allowed = AlsoCounted.GetValueOrDefault(reason, []);
        var others = Enum.GetValues<ImRefusal>().Where(r => r != reason && !allowed.Contains(r) && world.Counters.RefusedCount(r) > 0).ToArray();
        Assert.True(others.Length == 0, $"{reason}'s scenario also counted: " + string.Join(", ", others));
        Assert.Empty(world.Board.Entries);
    }
}
