using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Im;
using Im.Trust;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests;

/// <summary>
/// Transport encryption and resource limits (DMP chapter 05, "Transport encryption", "Resource limits"): TLS 1.3 with mutual
/// certificates from the new authority, bound to the IM identities, over the simulator's in-memory connection. Nothing touches a
/// network. Deadlines run on simulated time: a test moves the clock and waits for the gate to finish serving, never on a sleep.
/// </summary>
[Trait("Category", "im")]
public sealed class ImTransportTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxWorld world = new(Start);

    public void Dispose() => world.Dispose();

    static ImHeartbeat Beat(ulong sequence = 1) => new(sequence, ImHeartbeatStatus.Healthy, "0.1.0");

    // ---- accepted -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_valid_device_connects_over_TLS_1_3_and_its_heartbeat_is_on_the_board_marked_verified()
    {
        var device = world.EnrolDevice();
        var (connection, served) = await device.ConnectTlsAsync();

        await connection.HeartbeatAsync(Beat(3), CancellationToken.None);

        Assert.Equal(SslProtocols.Tls13, connection.Protocol);
        var entry = Assert.Single(world.Board.Entries);
        Assert.Equal(device.Subject, entry.Subject);
        Assert.Equal(3UL, entry.Sequence);
        Assert.True(entry.Verified);
        Assert.Equal(1, world.Counters.AcceptedCount(ImAccepted.Session));
        Assert.Equal(0, world.Counters.TotalRefused);

        await connection.DisposeAsync();
        await served;
        Assert.Equal(0, world.Transport.Active);
        Assert.Equal(0, world.Counters.TotalRefused);
    }

    [Fact]
    public async Task Many_heartbeats_travel_in_one_encrypted_session()
    {
        var device = world.EnrolDevice();
        var (connection, served) = await device.ConnectTlsAsync();
        for (ulong n = 1; n <= 5; n++)
        {
            world.Clock.Advance(TimeSpan.FromSeconds(30));
            await connection.HeartbeatAsync(Beat(n), CancellationToken.None);
        }
        Assert.Equal(5UL, Assert.Single(world.Board.Entries).Sequence);
        await connection.DisposeAsync();
        await served;
    }

    [Fact]
    public async Task On_the_wire_neither_the_token_the_payload_nor_the_knock_appears_in_clear()
    {
        var device = world.EnrolDevice();
        var (end, served) = world.ConnectStream();
        var tap = new TapStream(end);
        var connection = await ImTlsClientConnection.ConnectAsync(tap, device.Key, world.Anchors, device.Token, world.KnockSecret, world.Clock,
            world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, CancellationToken.None);
        await connection.HeartbeatAsync(new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "canary-agent-7"), CancellationToken.None);
        await connection.DisposeAsync();
        await served;

        var wire = tap.Captured;
        Assert.True(wire.Length > 1000, "the capture should hold the whole exchange");
        Assert.False(Contains(wire, device.Token), "the token travelled in clear");
        Assert.False(Contains(wire, "canary-agent-7"u8.ToArray()), "the heartbeat travelled in clear");
        Assert.False(Contains(wire, "IMM"u8.ToArray()) && Contains(wire, "IMT"u8.ToArray()), "IM structures are visible on the wire");
        Assert.False(Contains(wire, device.Key.Certificate.RawData), "the device certificate travelled in clear (TLS 1.3 encrypts it)");
        Assert.True(world.Board.Entries.Single().Verified);
    }

    // ---- refused by TLS ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_machine_without_a_device_certificate_gets_through_TLS_and_the_knock_but_its_response_is_refused()
    {
        var device = world.EnrolDevice(); // it has a key and a token, but shows no certificate in TLS
        var (end, served) = world.ConnectStream();
        await using var tls = new SslStream(end, leaveInnerStreamOpen: false);
        await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(null, world.Anchors, world.Clock));

        var client = device.NewClient();
        await ImStreamFraming.WriteAsync(tls, client.Knock(world.KnockSecret), CancellationToken.None);
        var accept = await ImStreamFraming.ReadAsync(tls, world.Settings.MaxFrameBytes, CancellationToken.None);
        Assert.Equal(ImFrameType.Accept, ImFrames.TypeOf(accept.Frame)); // the enrolment door is open here (next test)
        Assert.Equal(1, world.Transport.Uncertified);
        await ImStreamFraming.WriteAsync(tls, client.Respond(accept.Frame!), CancellationToken.None);
        var answer = await ImStreamFraming.ReadAsync(tls, world.Settings.MaxFrameBytes, CancellationToken.None);

        Assert.Equal(ImFrames.Refused(), answer.Frame);
        await served;
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.TransportNoCertificate));
        Assert.Equal(0, world.Counters.AcceptedCount(ImAccepted.Session));
        Assert.Equal(0, world.Transport.Uncertified);
    }

    [Fact]
    public async Task A_device_connects_with_a_TLS_certificate_already_joined_to_its_key_and_the_certificate_stays_the_callers()
    {
        var device = world.EnrolDevice();
        using var joined = ImTlsIdentity.Of(device.Key); // stands in for a certificate joined to a TPM key, which cannot be exported
        var bound = ImTlsIdentity.Bound(joined.Certificate, device.Key.Chain);
        var (end, served) = world.ConnectStream();

        var connection = await ImTlsClientConnection.ConnectAsync(end, device.Key, world.Anchors, device.Token, world.KnockSecret, world.Clock,
            world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, CancellationToken.None, bound);
        await connection.HeartbeatAsync(Beat(), CancellationToken.None);
        await connection.DisposeAsync();
        await served;

        Assert.Single(world.Board.Entries);
        Assert.NotNull(joined.Certificate.GetECDsaPrivateKey()); // not disposed with the connection
    }

    [Fact]
    public async Task A_TLS_identity_with_another_key_than_the_device_key_is_refused_before_a_byte_is_sent()
    {
        var device = world.EnrolDevice();
        using var other = ImTlsIdentity.Of(world.EnrolDevice().Key);
        var (end, served) = world.ConnectStream();

        await Assert.ThrowsAsync<ArgumentException>(() => ImTlsClientConnection.ConnectAsync(end, device.Key, world.Anchors, device.Token, world.KnockSecret,
            world.Clock, world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, CancellationToken.None, ImTlsIdentity.Bound(other.Certificate, [])));

        await end.DisposeAsync();
        await served;
        Assert.Equal(0, world.Counters.AcceptedCount(ImAccepted.Session));
    }

    [Fact]
    public async Task The_device_refuses_a_gate_whose_TLS_certificate_is_of_a_foreign_authority_before_any_IM_byte()
    {
        var device = world.EnrolDevice();
        var fakeGate = world.Foreign.Issue(TrustRole.MilkyWayGate, "milky-way", Start.AddDays(-1), Start.AddDays(30));
        using var fakeIdentity = ImTlsIdentity.Of(fakeGate);
        var (deviceEnd, gateEnd) = InMemoryDuplex.Pair();
        var fake = Task.Run(async () =>
        {
            await using var tls = new SslStream(gateEnd, leaveInnerStreamOpen: false);
            try
            {
                await tls.AuthenticateAsServerAsync(ImTls.ServerOptions(fakeIdentity, world.Anchors, world.Clock));
                return await ImStreamFraming.ReadAsync(tls, 1024, CancellationToken.None);
            }
            catch (Exception e) when (e is AuthenticationException or IOException)
            {
                return new ImStreamRead(ImStreamOutcome.Closed, null);
            }
        });

        var refused = await Assert.ThrowsAsync<ImClientException>(() => ImTlsClientConnection.ConnectAsync(deviceEnd, device.Key, world.Anchors, device.Token,
            world.KnockSecret, world.Clock, world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, CancellationToken.None));

        Assert.Equal(ImClientError.TransportRefused, refused.Error);
        Assert.NotEqual(ImStreamOutcome.Frame, (await fake).Outcome); // the fake gate never got a knock
    }

    [Fact]
    public async Task A_machine_without_a_device_certificate_reaches_the_enrolment_door_over_TLS()
    {
        var door = new ScriptedDoor(_ => "welcome"u8.ToArray());
        using var withDoor = new SandboxWorld(Start, door: door);
        var (end, served) = withDoor.ConnectStream();
        await using (var tls = new SslStream(end, leaveInnerStreamOpen: false))
        {
            await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(null, withDoor.Anchors, withDoor.Clock));
            var client = new ImClientSession(withDoor.Anchors, withDoor.Clock);
            await ImStreamFraming.WriteAsync(tls, client.Knock(withDoor.KnockSecret), CancellationToken.None);
            client.CheckGate((await ImStreamFraming.ReadAsync(tls, withDoor.Settings.MaxFrameBytes, CancellationToken.None)).Frame!);

            await ImStreamFraming.WriteAsync(tls, client.Door("enrol me"u8), CancellationToken.None);
            var reply = await ImStreamFraming.ReadAsync(tls, withDoor.Settings.MaxFrameBytes, CancellationToken.None);

            Assert.Equal("welcome"u8.ToArray(), ImClientSession.ReadDoorReply(reply.Frame));
            Assert.Equal("enrol me"u8.ToArray(), Assert.Single(door.Seen));
        }
        await served;
        Assert.Equal(0, withDoor.Counters.AcceptedCount(ImAccepted.Session));
        Assert.Empty(withDoor.Board.Entries);
    }

    [Fact]
    public async Task Connections_without_a_certificate_have_their_own_smaller_limit()
    {
        using var small = new SandboxWorld(Start, new MilkyWaySettings { MaxConnections = 10, MaxUncertifiedConnections = 2 });
        var holders = new List<(SslStream Tls, Task Served)>();
        for (var i = 0; i < 2; i++)
        {
            var (end, served) = small.ConnectStream();
            var tls = new SslStream(end, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(null, small.Anchors, small.Clock));
            holders.Add((tls, served));
        }
        var (extraEnd, extraServed) = small.ConnectStream();
        await TryRawTlsAsync(extraEnd, certificate: null, SslProtocols.Tls13, small);
        await extraServed;
        Assert.Equal(1, small.Counters.RefusedCount(ImRefusal.ConnectionLimit));

        // An enrolled device still gets in: the certificate-less connections cannot use up its slots.
        var device = small.EnrolDevice();
        var (connection, deviceServed) = await device.ConnectTlsAsync();
        await connection.HeartbeatAsync(Beat(), CancellationToken.None);
        await connection.DisposeAsync();
        await deviceServed;

        foreach (var (tls, served) in holders)
        {
            await tls.DisposeAsync();
            await served;
        }
        Assert.Equal(0, small.Transport.Uncertified);
        await extraEnd.DisposeAsync();
    }

    [Fact]
    public async Task A_device_certificate_of_a_foreign_authority_is_refused_by_TLS()
    {
        var device = world.EnrolDevice();
        device.Key = world.Foreign.Issue(TrustRole.Device, device.Subject, Start.AddDays(-1), Start.AddDays(30));

        var error = await Assert.ThrowsAsync<ImClientException>(() => device.ConnectTlsAsync());

        Assert.Contains(error.Error, new[] { ImClientError.TransportRefused, ImClientError.Closed });
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.UntrustedDevice));
        Assert.Equal(0, world.Counters.AcceptedCount(ImAccepted.Session));
    }

    [Fact]
    public async Task A_certificate_of_another_role_is_refused_by_TLS_even_with_client_authentication()
    {
        var device = world.EnrolDevice();
        // A Server certificate of the right authority that also carries client authentication: the role decides, not the EKU for TLS.
        device.Key = world.Authority.Issue(TrustRole.Server, device.Subject, Start.AddDays(-1), Start.AddDays(30), extraEku: [TrustOids.ClientAuth]);

        await Assert.ThrowsAsync<ImClientException>(() => device.ConnectTlsAsync());

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.UntrustedDevice));
    }

    [Fact]
    public async Task An_expired_device_certificate_is_refused_by_TLS()
    {
        var device = world.EnrolDevice();
        device.Key = world.Authority.Issue(TrustRole.Device, device.Subject, Start.AddDays(-30), Start.AddDays(-1));

        await Assert.ThrowsAsync<ImClientException>(() => device.ConnectTlsAsync());

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.TransportCertificateExpired));
    }

    [Theory]
    [InlineData(SslProtocols.Tls12)]
    public async Task An_older_TLS_version_is_refused(SslProtocols version)
    {
        var device = world.EnrolDevice();
        var (end, served) = world.ConnectStream();
        await TryRawTlsAsync(end, device.Key, version);
        await served;
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.TransportRefused));
        await end.DisposeAsync();
    }

    [Fact]
    public async Task Bytes_that_are_not_TLS_are_refused_and_counted()
    {
        var (end, served) = world.ConnectStream();
        await end.WriteAsync("GET / HTTP/1.1\r\n\r\n"u8.ToArray());
        await served;
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.TransportRefused));
        Assert.Equal(0, world.Counters.AcceptedCount(ImAccepted.Session));
        await end.DisposeAsync();
    }

    // ---- the binding of TLS to IM -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_response_with_another_device_certificate_than_TLS_showed_is_refused()
    {
        var shown = world.EnrolDevice();   // its certificate goes to TLS
        var speaking = world.EnrolDevice(); // its certificate, key and token go into IM
        var (end, served) = world.ConnectStream();
        await using var tls = new SslStream(end, leaveInnerStreamOpen: false);
        using var identity = ImTlsIdentity.Of(shown.Key);
        await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, world.Anchors, world.Clock));

        var client = speaking.NewClient();
        await ImStreamFraming.WriteAsync(tls, client.Knock(world.KnockSecret), CancellationToken.None);
        var accept = await ImStreamFraming.ReadAsync(tls, world.Settings.MaxFrameBytes, CancellationToken.None);
        await ImStreamFraming.WriteAsync(tls, client.Respond(accept.Frame!), CancellationToken.None);
        var answer = await ImStreamFraming.ReadAsync(tls, world.Settings.MaxFrameBytes, CancellationToken.None);

        Assert.Equal(ImFrames.Refused(), answer.Frame);
        await served;
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.TransportMismatch));
        Assert.Equal(0, world.Counters.AcceptedCount(ImAccepted.Session));
    }

    [Fact]
    public void The_device_refuses_a_gate_whose_accept_names_another_certificate_than_TLS_showed()
    {
        var device = world.EnrolDevice();
        using var otherGate = new GateIdentityOf(world.Authority.Issue(TrustRole.MilkyWayGate, "milky-way-gate", Start.AddDays(-1), Start.AddDays(30)));
        var client = new ImClientSession(world.Anchors, device.Key, device.Token, world.Clock, world.Settings.MaxPayloadBytes, otherGate.Identity.KeyId);
        var connection = world.Connect();

        var accept = connection.Send(client.Knock(world.KnockSecret));
        var error = Assert.Throws<ImClientException>(() => client.Respond(accept.Frame!));

        Assert.Equal(ImClientError.TransportMismatch, error.Error);
    }

    // ---- deadlines and limits (resource limits on the sandbox) ---------------------------------------------------------

    [Fact]
    public async Task A_connection_that_sends_nothing_is_closed_at_the_handshake_deadline()
    {
        var (end, served) = world.ConnectStream();
        Assert.Equal(1, world.Transport.Active);

        world.Clock.Advance(world.Settings.HandshakeTimeout + TimeSpan.FromSeconds(1));
        await served;

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.HandshakeTimeout));
        Assert.Equal(0, world.Transport.Active);
        await end.DisposeAsync();
    }

    [Fact]
    public async Task A_connection_that_stalls_after_TLS_is_closed_at_the_handshake_deadline_without_an_IM_byte()
    {
        var device = world.EnrolDevice();
        var (end, served) = world.ConnectStream();
        var tls = new SslStream(end, leaveInnerStreamOpen: false);
        using var identity = ImTlsIdentity.Of(device.Key);
        await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, world.Anchors, world.Clock));

        world.Clock.Advance(world.Settings.HandshakeTimeout + TimeSpan.FromSeconds(1));
        await served;

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.HandshakeTimeout));
        var read = await ReadAfterCloseAsync(tls);
        Assert.Equal(ImStreamOutcome.Closed, read); // no IM byte before a valid knock: the quiet answer
        await tls.DisposeAsync();
    }

    [Fact]
    public async Task An_established_session_that_falls_silent_is_closed_at_the_idle_deadline()
    {
        var device = world.EnrolDevice();
        var (connection, served) = await device.ConnectTlsAsync();
        await connection.HeartbeatAsync(Beat(), CancellationToken.None);

        world.Clock.Advance(world.Settings.IdleTimeout - TimeSpan.FromSeconds(1));
        await connection.HeartbeatAsync(Beat(2), CancellationToken.None); // still alive: the deadline moved with the heartbeat
        world.Clock.Advance(world.Settings.IdleTimeout + TimeSpan.FromSeconds(1));
        await served;

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.IdleTimeout));
        Assert.Equal(2UL, Assert.Single(world.Board.Entries).Sequence);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connections_beyond_the_limit_are_closed_without_a_byte_and_the_slots_come_back_at_the_deadline()
    {
        using var small = new SandboxWorld(Start, new MilkyWaySettings { MaxConnections = 3, MaxUncertifiedConnections = 3 });
        var slow = Enumerable.Range(0, 3).Select(_ => small.ConnectStream()).ToArray();
        Assert.Equal(3, small.Transport.Active);

        var (extra, extraServed) = small.ConnectStream();
        await extraServed;
        Assert.Equal(1, small.Counters.RefusedCount(ImRefusal.ConnectionLimit));
        Assert.Equal(0, await extra.ReadAsync(new byte[1]));

        small.Clock.Advance(small.Settings.HandshakeTimeout + TimeSpan.FromSeconds(1));
        await Task.WhenAll(slow.Select(s => s.Served));
        Assert.Equal(0, small.Transport.Active);
        Assert.Equal(3, small.Counters.RefusedCount(ImRefusal.HandshakeTimeout));

        var device = small.EnrolDevice();
        var (connection, served) = await device.ConnectTlsAsync();
        await connection.HeartbeatAsync(Beat(), CancellationToken.None);
        await connection.DisposeAsync();
        await served;
        foreach (var s in slow) await s.DeviceEnd.DisposeAsync();
        await extra.DisposeAsync();
    }

    [Fact]
    public async Task An_oversize_length_prefix_is_refused_before_the_frame_is_read()
    {
        var device = world.EnrolDevice();
        var (end, served) = world.ConnectStream();
        var tls = new SslStream(end, leaveInnerStreamOpen: false);
        using var identity = ImTlsIdentity.Of(device.Key);
        await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, world.Anchors, world.Clock));

        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)world.Settings.MaxFrameBytes + 1);
        await tls.WriteAsync(prefix);
        await tls.FlushAsync();
        await served;

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Oversize));
        Assert.Equal(ImStreamOutcome.Closed, await ReadAfterCloseAsync(tls)); // before the knock: quiet
        await tls.DisposeAsync();
    }

    [Fact]
    public async Task A_frame_cut_short_by_the_peer_is_counted_as_malformed()
    {
        var device = world.EnrolDevice();
        var (end, served) = world.ConnectStream();
        var tls = new SslStream(end, leaveInnerStreamOpen: false);
        using var identity = ImTlsIdentity.Of(device.Key);
        await tls.AuthenticateAsClientAsync(ImTls.ClientOptions(identity, world.Anchors, world.Clock));

        var cut = new byte[4 + 10];
        BinaryPrimitives.WriteUInt32BigEndian(cut, 73);
        await tls.WriteAsync(cut);
        await tls.FlushAsync();
        await tls.ShutdownAsync();
        await served;

        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.Malformed));
        await tls.DisposeAsync();
    }

    [Fact]
    public async Task What_the_gate_counts_for_transport_refusals_holds_no_content()
    {
        var foreign = world.EnrolDevice();
        foreign.Key = world.Foreign.Issue(TrustRole.Device, foreign.Subject, Start.AddDays(-1), Start.AddDays(30));
        await Assert.ThrowsAsync<ImClientException>(() => foreign.ConnectTlsAsync());
        var (end, served) = world.ConnectStream();
        await end.WriteAsync("canary-probe"u8.ToArray());
        await served;

        var counted = string.Join("\n", world.Counters.Snapshot().Select(row => $"{row.What} {row.Count}"));
        Assert.DoesNotContain(foreign.Subject, counted, StringComparison.Ordinal);
        Assert.DoesNotContain("canary", counted, StringComparison.Ordinal);
        Assert.DoesNotContain("CN=", counted, StringComparison.Ordinal);
        await end.DisposeAsync();
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>A TLS client that is not Doppler's agent: optional certificate, any protocol version. Failure is expected and swallowed.</summary>
    async Task TryRawTlsAsync(Stream end, IImSigner? certificate, SslProtocols protocols, SandboxWorld? in_ = null)
    {
        var w = in_ ?? world;
        using var identity = certificate is null ? null : ImTlsIdentity.Of(certificate);
        var options = ImTls.ClientOptions(identity, w.Anchors, w.Clock);
        options.EnabledSslProtocols = protocols;
        var tls = new SslStream(end, leaveInnerStreamOpen: true);
        try
        {
            await tls.AuthenticateAsClientAsync(options);
            // In TLS 1.3 the client may finish before the gate has judged its certificate; the gate's verdict arrives on the first read.
            _ = await tls.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false);
        }
        catch (Exception e) when (e is AuthenticationException or IOException)
        {
        }
    }

    static async Task<ImStreamOutcome> ReadAfterCloseAsync(Stream tls)
    {
        try
        {
            return (await ImStreamFraming.ReadAsync(tls, 1024, CancellationToken.None)).Outcome;
        }
        catch (IOException)
        {
            return ImStreamOutcome.Closed;
        }
    }

    static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>Records every byte that passes in either direction, as a wire capture would.</summary>
    sealed class TapStream(Stream inner) : Stream
    {
        readonly MemoryStream captured = new();
        readonly Lock gate = new();

        public byte[] Captured
        {
            get { lock (gate) return captured.ToArray(); }
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            Record(buffer.AsSpan(offset, n));
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            Record(buffer.Span[..n]);
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Record(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Record(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        void Record(ReadOnlySpan<byte> bytes)
        {
            lock (gate) captured.Write(bytes);
        }
    }

    sealed class GateIdentityOf(IImSigner signer) : IDisposable
    {
        public ImTlsIdentity Identity { get; } = ImTlsIdentity.Of(signer);

        public void Dispose() => Identity.Dispose();
    }
}
