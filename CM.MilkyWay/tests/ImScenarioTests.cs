using Im;
using Im.Trust;
using CM.MilkyWay.Simulation;
using CM.MilkyWay.Verify;
using Xunit;

namespace CM.MilkyWay.Tests;

/// <summary>
/// The acceptance test of IM version 0 (DMP chapter 05, "Tests: the im group"), run in the sandbox world on simulated time: a valid
/// device's heartbeat is on the board marked verified; expired, forged, unenrolled, replayed and wrong-permission messages are refused
/// and counted. Each test names the verify rule it proves.
/// </summary>
[Trait("Category", "im")]
public sealed class ImScenarioTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxWorld world = new(Start);

    public void Dispose() => world.Dispose();

    static ImHeartbeat Beat(ulong sequence = 1) => new(sequence, ImHeartbeatStatus.Healthy, "0.1.0");

    (SimulatedConnection Connection, ImClientSession Client) Established(SimulatedDevice device)
    {
        var (connection, client) = device.Handshake();
        Assert.True(connection.Session.IsEstablished, "the handshake should succeed for this device");
        return (connection, client);
    }

    static byte[] Sealed(SimulatedDevice device, byte[] token, IImSigner key, SandboxWorld world, string kind = ImKinds.Heartbeat, byte[]? payload = null) =>
        ImFrames.Message(ImEnvelopeCodec.Seal(kind, payload ?? Beat().Encode(), token, key, world.Clock.GetUtcNow(), world.Settings.MaxPayloadBytes));

    void AssertRefused(SimulatedConnection connection, byte[] frame, ImRefusal reason)
    {
        var before = world.Counters.RefusedCount(reason);
        var reply = connection.Send(frame);
        Assert.True(reply.Close);
        Assert.Equal(ImFrames.Refused(), reply.Frame);
        Assert.Equal(before + 1, world.Counters.RefusedCount(reason));
    }

    void AssertHandshakeRefused(SimulatedDevice device, ImRefusal reason)
    {
        var before = world.Counters.RefusedCount(reason);
        var (connection, _) = device.Handshake();
        Assert.False(connection.Session.IsEstablished);
        Assert.Equal(before + 1, world.Counters.RefusedCount(reason));
        Assert.Equal(ImFrames.Refused(), connection.Received[^1]);
    }

    // ---- accepted -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_valid_device_completes_the_handshake_and_its_heartbeat_is_on_the_board_marked_verified()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Established(device);

        var reply = connection.Send(client.Heartbeat(Beat(7)));

        Assert.False(reply.Close);
        Assert.Equal(ImFrameType.Ack, ImFrames.TypeOf(reply.Frame));
        var entry = Assert.Single(world.Board.Entries);
        Assert.Equal(device.Subject, entry.Subject);
        Assert.Equal(EnrolledRole.Laptop, entry.Role);
        Assert.Equal(7UL, entry.Sequence);
        Assert.True(entry.Verified);
        Assert.Equal(1, world.Counters.AcceptedCount(ImAccepted.Session));
        Assert.Equal(1, world.Counters.AcceptedCount(ImAccepted.Heartbeat));
        Assert.Equal(0, world.Counters.TotalRefused);
    }

    [Fact]
    public void A_verified_message_names_the_token_it_carried()
    {
        var device = world.EnrolDevice();
        var (_, client) = Established(device);
        var result = world.Gate.Verifier.Verify(ImFrames.MessageBody(client.Heartbeat(Beat())));
        Assert.Equal(Convert.ToHexStringLower(ImTokenCodec.Parse(device.Token).TokenId), result.Message!.TokenId);
    }

    [Fact]
    public void A_session_carries_many_heartbeats_and_the_board_keeps_the_newest()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Established(device);
        for (ulong i = 1; i <= 5; i++)
        {
            Assert.False(connection.Send(client.Heartbeat(Beat(i))).Close);
            world.Clock.Advance(TimeSpan.FromSeconds(30));
        }
        Assert.Equal(5UL, world.Board.Find(device.Subject)!.Sequence);
        Assert.Equal(5, world.Counters.AcceptedCount(ImAccepted.Heartbeat));
    }

    // ---- expired (rules 4, 7, 8) -----------------------------------------------------------------------------------

    [Fact]
    public void Rule_8_a_token_that_expires_during_the_session_is_refused_as_expired()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, expiresAt: Start.AddMinutes(10));
        var (connection, client) = Established(device);
        world.Clock.Advance(TimeSpan.FromMinutes(11));

        AssertRefused(connection, client.Heartbeat(Beat()), ImRefusal.TokenExpired);
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void Rule_8_an_expired_token_is_refused_at_the_handshake()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, notBefore: Start.AddHours(-3), expiresAt: Start.AddHours(-1));
        AssertHandshakeRefused(device, ImRefusal.TokenExpired);
    }

    [Fact]
    public void Rule_7_a_token_that_is_not_yet_valid_is_refused()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, notBefore: Start.AddHours(1), expiresAt: Start.AddHours(2));
        AssertHandshakeRefused(device, ImRefusal.TokenNotYetValid);
    }

    [Fact]
    public void Rule_7_a_token_valid_within_the_clock_skew_is_accepted()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, notBefore: Start.AddSeconds(90), expiresAt: Start.AddHours(2));
        Established(device);
    }

    [Fact]
    public void Rule_6_a_token_longer_than_the_maximum_lifetime_is_refused()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, notBefore: Start.AddMinutes(-1), expiresAt: Start.AddDays(3));
        AssertHandshakeRefused(device, ImRefusal.TokenPolicy);
    }

    [Fact]
    public void Rule_4_a_token_signed_by_an_expired_signing_certificate_is_refused()
    {
        var device = world.EnrolDevice();
        var shortLived = world.Authority.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Start.AddDays(-2), Start.AddHours(1));
        device.Token = world.IssueToken(device, signer: shortLived, expiresAt: Start.AddHours(4));
        var (connection, client) = Established(device);
        world.Clock.Advance(TimeSpan.FromHours(2));

        AssertRefused(connection, client.Heartbeat(Beat()), ImRefusal.CertificateExpired);
    }

    // ---- forged (rules 4, 5, 10, 11 and the handshake) ------------------------------------------------------------

    [Fact]
    public void Rule_4_a_token_signed_by_a_foreign_authority_is_refused_as_forged()
    {
        var device = world.EnrolDevice();
        var foreignSigner = world.Foreign.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Start.AddDays(-1), Start.AddDays(30));
        device.Token = world.IssueToken(device, signer: foreignSigner);
        AssertHandshakeRefused(device, ImRefusal.UntrustedTokenSigner);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusalFamily.Forged));
    }

    [Fact]
    public void Rule_4_a_token_signed_by_a_device_key_of_the_right_authority_is_refused()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, signer: device.Key);
        AssertHandshakeRefused(device, ImRefusal.UntrustedTokenSigner);
    }

    [Fact]
    public void Rule_4_a_token_signed_after_the_signing_certificate_was_revoked_is_refused()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Established(device);
        world.Authority.Revoke(world.TokenSigner.Certificate);

        AssertRefused(connection, client.Heartbeat(Beat()), ImRefusal.UntrustedTokenSigner);
    }

    [Fact]
    public void Rule_5_a_tampered_token_is_refused_even_when_the_sender_signs_the_envelope()
    {
        var device = world.EnrolDevice();
        var (connection, _) = Established(device);
        var parsed = ImTokenCodec.Parse(device.Token);
        var tampered = (byte[])device.Token.Clone();
        // The expiry is 8 bytes before the nonce, the nonce 16 before the chain; flip a byte of the expiry so the token lives longer.
        var expiryOffset = 4 + parsed.Body.Length - ChainLength(parsed) - 16 - 8;
        tampered[expiryOffset] ^= 0x01;

        AssertRefused(connection, Sealed(device, tampered, device.Key, world), ImRefusal.BadTokenSignature);
    }

    [Fact]
    public void Rule_11_a_message_changed_after_signing_is_refused()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Established(device);
        var frame = client.Heartbeat(Beat(1));
        frame[^(ImLimits.SignatureLength + 3)] ^= 0x20; // a byte of the agent version, inside the signed body

        AssertRefused(connection, frame, ImRefusal.BadMessageSignature);
    }

    [Fact]
    public void Rule_11_a_message_signed_by_another_key_than_the_token_names_is_refused()
    {
        var device = world.EnrolDevice();
        var other = world.EnrolDevice();
        var (connection, _) = Established(device);

        AssertRefused(connection, Sealed(device, device.Token, other.Key, world), ImRefusal.BadMessageSignature);
    }

    [Fact]
    public void Rule_10_a_genuine_message_of_another_device_inside_this_session_is_refused()
    {
        var device = world.EnrolDevice();
        var other = world.EnrolDevice();
        var (connection, _) = Established(device);

        AssertRefused(connection, Sealed(other, other.Token, other.Key, world), ImRefusal.SessionMismatch);
    }

    [Fact]
    public void Handshake_a_device_certificate_from_a_foreign_authority_is_refused()
    {
        var device = world.EnrolDevice();
        device.Key = world.Foreign.Issue(TrustRole.Device, device.Subject, Start.AddDays(-1), Start.AddDays(30));
        AssertHandshakeRefused(device, ImRefusal.UntrustedDevice);
    }

    [Fact]
    public void Handshake_a_revoked_device_certificate_is_refused()
    {
        var device = world.EnrolDevice();
        world.Authority.Revoke(device.Key.Certificate);
        AssertHandshakeRefused(device, ImRefusal.UntrustedDevice);
    }

    [Fact]
    public void Handshake_a_stolen_token_shown_with_another_device_certificate_is_refused()
    {
        var victim = world.EnrolDevice();
        var thief = world.EnrolDevice();
        thief.Token = victim.Token;
        AssertHandshakeRefused(thief, ImRefusal.DeviceKeyMismatch);
    }

    [Fact]
    public void Handshake_the_device_refuses_a_gate_whose_certificate_is_not_from_the_new_authority()
    {
        var impostorKey = world.Foreign.Issue(TrustRole.MilkyWayGate, "milky-way-gate", Start.AddDays(-1), Start.AddDays(30));
        var impostor = new Gate.MilkyWayGate(world.Settings, impostorKey, world.Foreign.Anchors(), world.Enrolment, world.Clock, new ImCounters(), world.Board);
        var device = world.EnrolDevice();
        var client = device.NewClient();
        var accept = impostor.Open().Receive(client.Knock(world.KnockSecret));

        var error = Assert.Throws<ImClientException>(() => client.Respond(accept.Frame!));
        Assert.Equal(ImClientError.UntrustedGate, error.Error);
    }

    [Fact]
    public void Handshake_the_device_refuses_an_accept_frame_changed_on_the_way()
    {
        var device = world.EnrolDevice();
        var client = device.NewClient();
        var accept = world.Connect().Send(client.Knock(world.KnockSecret)).Frame!;
        accept[5] ^= 0x01; // a byte of the challenge

        var error = Assert.Throws<ImClientException>(() => client.Respond(accept));
        Assert.Equal(ImClientError.BadGateSignature, error.Error);
    }

    // ---- unenrolled (rule 9) ---------------------------------------------------------------------------------------

    [Fact]
    public void Rule_9_a_device_unknown_to_Doppler_is_refused_as_unenrolled()
    {
        var device = world.UnenrolledDevice();
        AssertHandshakeRefused(device, ImRefusal.NotEnrolled);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusalFamily.Unenrolled));
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void Rule_9_a_device_revoked_during_the_session_is_refused()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Established(device);
        world.Enrolment.Revoke(device.Subject);

        AssertRefused(connection, client.Heartbeat(Beat()), ImRefusal.Revoked);
    }

    [Fact]
    public void Rule_9_an_enrolment_that_holds_another_key_or_role_is_refused()
    {
        var device = world.EnrolDevice();
        world.Enrolment.Put(new EnrolmentRecord(device.Subject, EnrolledRole.Laptop, new string('0', 64), false));
        AssertHandshakeRefused(device, ImRefusal.EnrolmentMismatch);

        world.Enrolment.Put(new EnrolmentRecord(device.Subject, EnrolledRole.Runner, device.Key.KeyId, false));
        AssertHandshakeRefused(device, ImRefusal.EnrolmentMismatch);
    }

    // ---- replayed and stale (rules 12, 16) -------------------------------------------------------------------------

    [Fact]
    public void Rule_16_the_same_message_is_accepted_once_and_refused_as_replayed_after()
    {
        var device = world.EnrolDevice();
        var (first, client) = Established(device);
        var frame = client.Heartbeat(Beat());
        Assert.False(first.Send(frame).Close);

        var (second, _) = Established(device);
        AssertRefused(second, frame, ImRefusal.Replayed);
        Assert.Equal(1, world.Counters.AcceptedCount(ImAccepted.Heartbeat));
    }

    [Fact]
    public void Rule_12_a_replay_after_the_window_is_refused_as_stale_so_forgetting_the_id_is_safe()
    {
        var device = world.EnrolDevice();
        var (first, client) = Established(device);
        var frame = client.Heartbeat(Beat());
        Assert.False(first.Send(frame).Close);
        world.Clock.Advance(world.Settings.ClockSkew + TimeSpan.FromMilliseconds(1));

        var (second, _) = Established(device);
        AssertRefused(second, frame, ImRefusal.Stale);
    }

    [Fact]
    public void Rule_12_a_message_from_a_clock_too_far_ahead_is_refused_as_stale()
    {
        var device = world.EnrolDevice();
        var (connection, _) = Established(device);
        var future = ImFrames.Message(ImEnvelopeCodec.Seal(ImKinds.Heartbeat, Beat().Encode(), device.Token, device.Key, Start.AddMinutes(5), world.Settings.MaxPayloadBytes));
        AssertRefused(connection, future, ImRefusal.Stale);
    }

    [Fact]
    public void Rule_16_a_full_replay_memory_fails_closed_until_ids_expire()
    {
        using var small = new SandboxWorld(Start, new MilkyWaySettings { ReplayCapacity = 2 });
        var device = small.EnrolDevice();
        var (connection, client) = device.Handshake();
        Assert.False(connection.Send(client.Heartbeat(Beat(1))).Close);
        Assert.False(connection.Send(client.Heartbeat(Beat(2))).Close);
        var reply = connection.Send(client.Heartbeat(Beat(3)));
        Assert.True(reply.Close);
        Assert.Equal(1, small.Counters.RefusedCount(ImRefusal.ReplayCacheFull));

        small.Clock.Advance(small.Settings.ClockSkew + TimeSpan.FromSeconds(1));
        var (again, client2) = device.Handshake();
        Assert.False(again.Send(client2.Heartbeat(Beat(4))).Close);
    }

    [Fact]
    public void Handshake_a_replayed_knock_gets_the_quiet_answer()
    {
        var device = world.EnrolDevice();
        var knock = device.NewClient().Knock(world.KnockSecret);
        Assert.NotNull(world.Connect().Send(knock).Frame);

        var replay = world.Connect();
        Assert.True(replay.Send(knock).IsQuiet);
        Assert.Empty(replay.Received);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.KnockReplayed));
    }

    // ---- wrong permission, unknown kind, payload (rules 13, 14, 15) ------------------------------------------------

    [Fact]
    public void Rule_14_a_token_without_the_heartbeat_permission_is_refused_as_wrong_permission()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, permissions: [ImKinds.SessionPermission]);
        var (connection, client) = Established(device);

        AssertRefused(connection, client.Heartbeat(Beat()), ImRefusal.WrongPermission);
        Assert.Empty(world.Board.Entries);
    }

    [Fact]
    public void Handshake_a_token_without_the_session_permission_is_refused_as_wrong_permission()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, permissions: [ImKinds.HeartbeatPermission]);
        AssertHandshakeRefused(device, ImRefusal.WrongPermission);
    }

    [Fact]
    public void Rule_13_a_kind_not_in_the_table_is_refused_even_when_the_token_lists_it()
    {
        var device = world.EnrolDevice();
        device.Token = world.IssueToken(device, permissions: [.. SandboxWorld.DefaultPermissions, "im.reboot"]);
        var (connection, client) = Established(device);

        AssertRefused(connection, client.Message("im.reboot", [1]), ImRefusal.UnknownKind);
    }

    [Fact]
    public void Rule_15_a_heartbeat_with_an_unknown_status_is_refused_as_malformed()
    {
        var device = world.EnrolDevice();
        var (connection, client) = Established(device);
        var payload = Beat().Encode();
        payload[9] = 7; // status byte after version (1) and sequence (8)

        AssertRefused(connection, client.Message(ImKinds.Heartbeat, payload), ImRefusal.Malformed);
    }

    [Fact]
    public void Rule_1_an_oversize_frame_is_refused_before_it_is_read()
    {
        var device = world.EnrolDevice();
        var (connection, _) = Established(device);
        AssertRefused(connection, new byte[world.Settings.MaxFrameBytes + 1], ImRefusal.Oversize);
    }

    // ---- the quiet answer and the session state -------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x47, 0x45, 0x54, 0x20, 0x2F, 0x20, 0x48, 0x54, 0x54, 0x50 })] // "GET / HTTP"
    [InlineData(new byte[] { 0x16, 0x03, 0x01, 0x00, 0x05 })] // the start of a TLS hello
    [InlineData(new byte[] { 0x10, 0x00 })] // a message frame before any handshake
    public void A_scanner_gets_the_quiet_answer(byte[] probe)
    {
        var connection = world.Connect();
        var reply = connection.Send(probe);
        Assert.True(reply.IsQuiet);
        Assert.Empty(connection.Received);
        Assert.True(connection.Session.IsClosed);
    }

    [Fact]
    public void A_knock_with_the_wrong_secret_gets_the_quiet_answer_and_is_counted()
    {
        var device = world.EnrolDevice();
        var connection = world.Connect();
        Assert.True(connection.Send(device.NewClient().Knock(new byte[32])).IsQuiet);
        Assert.Empty(connection.Received);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.KnockRefused));
    }

    [Fact]
    public void A_knock_from_a_clock_far_off_gets_the_quiet_answer()
    {
        var connection = world.Connect();
        Assert.True(connection.Send(ImFrames.Knock(world.KnockSecret, Start.AddMinutes(-10))).IsQuiet);
        Assert.Equal(1, world.Counters.RefusedCount(ImRefusal.KnockStale));
    }

    [Fact]
    public void A_previous_knock_secret_still_opens_the_door_during_a_rotation()
    {
        var previous = new byte[32];
        previous[0] = 1;
        using var rotating = new SandboxWorld(Start, new MilkyWaySettings { KnockSecrets = [new byte[32].Select((_, i) => (byte)(i + 7)).ToArray(), previous] });
        var device = rotating.EnrolDevice();
        Assert.NotNull(rotating.Connect().Send(device.NewClient().Knock(previous)).Frame);
    }

    [Fact]
    public void A_handshake_that_takes_too_long_is_refused()
    {
        var device = world.EnrolDevice();
        var connection = world.Connect();
        var client = device.NewClient();
        var accept = connection.Send(client.Knock(world.KnockSecret)).Frame!;
        world.Clock.Advance(world.Settings.HandshakeTimeout + TimeSpan.FromSeconds(1));

        AssertRefused(connection, client.Respond(accept), ImRefusal.HandshakeTimeout);
    }

    [Fact]
    public void A_message_frame_in_place_of_the_response_is_refused_and_closes_the_session()
    {
        var device = world.EnrolDevice();
        var connection = world.Connect();
        Assert.NotNull(connection.Send(device.NewClient().Knock(world.KnockSecret)).Frame);
        AssertRefused(connection, Sealed(device, device.Token, device.Key, world), ImRefusal.OutOfOrder);
        Assert.True(connection.Send(Sealed(device, device.Token, device.Key, world)).IsQuiet);
    }

    [Fact]
    public void The_gate_refuses_to_start_without_a_knock_secret()
    {
        var settings = new MilkyWaySettings();
        Assert.Throws<InvalidOperationException>(() =>
            new Gate.MilkyWayGate(settings, world.GateKey, world.Anchors, world.Enrolment, world.Clock, world.Counters, world.Board));
    }

    static int ChainLength(ImToken token) => 1 + token.SignerChain.Sum(c => 2 + c.Length);
}
