using System.Numerics;
using System.Net.Security;
using System.Security.Cryptography;
using Im;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests;

/// <summary>
/// The edges of the encoding's limits and alphabets, the client session's order, the stream framing and the TLS options (DMP chapters 04
/// and 05). Each test pins a value that is allowed exactly at its limit and refused one past it, so a limit that moves by one fails here.
/// Found missing by the mutation run of Supernova level 3 (DMP chapter 06).
/// </summary>
[Trait("Category", "im")]
public sealed class ImEdgeTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxAuthority ca = new("sandbox", Now.AddDays(-30));
    readonly IImSigner signer;
    readonly IImSigner device;

    public ImEdgeTests()
    {
        signer = ca.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Now.AddDays(-1), Now.AddDays(30));
        device = ca.Issue(TrustRole.Device, "d-0001", Now.AddDays(-1), Now.AddDays(30));
    }

    public void Dispose() => ca.Dispose();

    // ---- fields at their limit ----------------------------------------------------------------------------------------

    [Fact]
    public void Each_reader_field_takes_exactly_its_limit()
    {
        Assert.Equal(4, new ImReader(new ImWriter().Bytes16(new byte[4], 4).ToArray()).Bytes16(4).Length);
        Assert.Equal(4, new ImReader(new ImWriter().Bytes32(new byte[4], 4).ToArray()).Bytes32(4).Length);
        Assert.Empty(new ImReader(new ImWriter().Bytes16([], 4).ToArray()).Bytes16(4)); // an empty field is a field
        Assert.Equal("abcd", new ImReader(new ImWriter().Id8("abcd", ImAlphabet.Name, 4).ToArray()).Id8(ImAlphabet.Name, 4));
        Assert.Equal(3, new ImReader(new ImWriter().List16([[1], [2], [3]], 3, 4).ToArray()).List16(3, 4).Count);

        Malformed(() => new ImReader(new ImWriter().U32(5).Raw(new byte[5]).ToArray()).Bytes32(4));
        Malformed(() => new ImReader(new ImWriter().U8(5).Raw("abcde"u8).ToArray()).Id8(ImAlphabet.Name, 4));
        Malformed(() => new ImReader(new ImWriter().U8(4).U16(1).U8(1).U16(1).U8(2).U16(1).U8(3).U16(1).U8(4).ToArray()).List16(3, 4));
        Malformed(() => new ImReader(new ImWriter().U8(0).ToArray()).List16(3, 4));
    }

    [Fact]
    public void Each_writer_field_takes_exactly_its_limit_and_refuses_more()
    {
        Assert.Equal(2 + 4, new ImWriter().Bytes16(new byte[4], 4).ToArray().Length);
        Assert.Throws<ArgumentException>(() => new ImWriter().Bytes16(new byte[5], 4));
        Assert.Throws<ArgumentException>(() => new ImWriter().Bytes32(new byte[5], 4));
        Assert.Equal(1 + 3 * 3, new ImWriter().List16([[1], [2], [3]], 3, 4).ToArray().Length);
        Assert.Throws<ArgumentException>(() => new ImWriter().List16([], 3, 4));
        Assert.Throws<ArgumentException>(() => new ImWriter().List16([[1], [2], [3], [4]], 3, 4));
    }

    [Theory]
    [InlineData("a"), InlineData("z"), InlineData("0"), InlineData("9"), InlineData("."), InlineData("-"), InlineData("a.b-9")]
    public void A_name_takes_lower_case_letters_digits_dot_and_dash(string name) => Assert.True(ImText.IsValid(name, ImAlphabet.Name, 64));

    [Theory]
    [InlineData("A"), InlineData("`"), InlineData("{"), InlineData("/"), InlineData(":"), InlineData("_"), InlineData(" ")]
    public void A_name_refuses_the_characters_next_to_its_ranges(string name) => Assert.False(ImText.IsValid(name, ImAlphabet.Name, 64));

    [Theory]
    [InlineData("a", true), InlineData("z", true), InlineData("0", true), InlineData("9", true), InlineData("-", true), InlineData(".", false), InlineData("{", false), InlineData("/", false)]
    public void A_subject_is_a_name_without_the_dot(string subject, bool valid) => Assert.Equal(valid, ImText.IsValid(subject, ImAlphabet.Subject, 64));

    [Theory]
    [InlineData("!", true), InlineData("~", true), InlineData("0.1.0+abc", true), InlineData(" ", false), InlineData("\u007f", false)]
    public void Printable_is_the_visible_ASCII_range(string text, bool valid) => Assert.Equal(valid, ImText.IsValid(text, ImAlphabet.Printable, 32));

    [Fact]
    public void A_token_with_exactly_the_most_permissions_is_issued_and_parsed()
    {
        var names = Enumerable.Range(0, ImLimits.PermissionsMax).Select(i => $"p.{i:D2}").ToArray();
        var claims = new ImTokenClaims("d-0001", EnrolledRole.Laptop, ImKeys.SubjectPublicKeyInfo(device.Certificate), names, Now, Now.AddHours(8));
        Assert.Equal(ImLimits.PermissionsMax, ImTokenCodec.Parse(ImTokenCodec.Issue(claims, signer, Now)).Permissions.Count);
        Assert.Throws<ArgumentException>(() => ImTokenCodec.Issue(claims with { Permissions = [.. names, "p.99"] }, signer, Now));
    }

    [Fact]
    public void A_token_of_the_largest_size_travels_in_an_envelope_and_a_response()
    {
        var token = LargestToken();
        Assert.Equal(4 + ImLimits.TokenMax + ImLimits.SignatureLength, token.Length);
        Assert.NotNull(ImTokenCodec.Parse(token));

        var envelope = ImEnvelopeCodec.Seal(ImKinds.Heartbeat, new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "0.1.0").Encode(), token, device, Now, 4096);
        Assert.Equal(token, ImEnvelopeCodec.Parse(envelope, 4096).Token);

        var knock = ImFrames.Knock(new byte[32], Now);
        var accept = ImFrames.Accept(knock, new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], signer);
        Assert.Equal(token, ImFrames.ParseResponse(ImFrames.Response(knock, accept, token, device)).Token);

        Malformed(() => ImTokenCodec.Parse(token.Concat(new byte[] { 0 }).ToArray()));
    }

    [Fact]
    public void The_last_heartbeat_status_parses_and_the_one_after_it_does_not()
    {
        var failing = new ImHeartbeat(1, ImHeartbeatStatus.Failing, "0.1.0").Encode();
        Assert.Equal(ImHeartbeatStatus.Failing, ImHeartbeat.Parse(failing).Status);
        var after = failing.ToArray();
        after[1 + 8] = (byte)ImHeartbeatStatus.Failing + 1; // the status byte follows the version and the 8-byte sequence
        Malformed(() => ImHeartbeat.Parse(after));
    }

    // ---- low s at the half order ---------------------------------------------------------------------------------------

    [Fact]
    public void An_s_of_exactly_half_the_order_is_low_and_one_more_is_not()
    {
        var order = new BigInteger(Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"), isUnsigned: true, isBigEndian: true);
        Assert.True(ImKeys.IsLowS(Signature(order / 2)));
        Assert.False(ImKeys.IsLowS(Signature(order / 2 + 1)));
        Assert.False(ImKeys.IsLowS(new byte[63]));

        var high = Signature(order - 1);
        var low = ImKeys.ToLowS(high);
        Assert.Equal(64, low.Length);
        Assert.Equal(Signature(1), low); // n - (n - 1) = 1, right-aligned in its 32 bytes
    }

    static byte[] Signature(BigInteger s)
    {
        var result = new byte[64];
        result[0] = 1; // any r
        var bytes = s.ToByteArray(isUnsigned: true, isBigEndian: true);
        bytes.CopyTo(result, 64 - bytes.Length);
        return result;
    }

    [Fact]
    public void A_canonical_P256_key_has_no_trailing_byte()
    {
        var key = ImKeys.SubjectPublicKeyInfo(device.Certificate);
        Assert.True(ImKeys.IsCanonicalP256(key));
        Assert.False(ImKeys.IsCanonicalP256(key.Concat(new byte[] { 0 }).ToArray()));
    }

    // ---- frames and the client session's order ------------------------------------------------------------------------

    [Fact]
    public void A_message_frame_needs_its_type_and_at_least_one_byte_of_body()
    {
        Assert.Equal(1, ImFrames.MessageBody(new byte[] { (byte)ImFrameType.Message, 7 }).Length);
        Malformed(() => ImFrames.MessageBody(new byte[] { (byte)ImFrameType.Message }));
        Malformed(() => ImFrames.MessageBody(new byte[] { (byte)ImFrameType.Ack, 7 }));
    }

    [Fact]
    public void The_client_answers_the_challenge_once_and_sends_nothing_before_it_is_established()
    {
        var token = ImTokenCodec.Issue(new ImTokenClaims("d-0001", EnrolledRole.Laptop, ImKeys.SubjectPublicKeyInfo(device.Certificate),
            [ImKinds.HeartbeatPermission, ImKinds.SessionPermission], Now, Now.AddHours(8)), signer, Now);
        var trust = ca.Anchors();
        var gate = ca.Issue(TrustRole.MilkyWayGate, "milky-way", Now.AddDays(-1), Now.AddDays(30));
        var client = new ImClientSession(trust, device, token, new FixedTime(Now));
        var knock = client.Knock(new byte[32]);
        var accept = ImFrames.Accept(knock, new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], gate);

        Assert.Equal(ImClientError.OutOfOrder, Assert.Throws<ImClientException>(() => client.Heartbeat(new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "0.1.0"))).Error);
        Assert.NotNull(client.Respond(accept));
        Assert.Equal(ImClientError.OutOfOrder, Assert.Throws<ImClientException>(() => client.Respond(token, device)).Error);
        Assert.Equal(ImClientError.OutOfOrder, Assert.Throws<ImClientException>(() => client.Door("x"u8)).Error);

        var keyless = new ImClientSession(trust, new FixedTime(Now));
        keyless.CheckGate(ImFrames.Accept(keyless.Knock(new byte[32]), new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], gate));
        Assert.Equal(ImClientError.OutOfOrder, Assert.Throws<ImClientException>(() => keyless.Respond(accept)).Error); // checked once only
    }

    [Fact]
    public void The_client_knocks_once_and_says_when_the_gate_was_checked()
    {
        var gate = ca.Issue(TrustRole.MilkyWayGate, "milky-way", Now.AddDays(-1), Now.AddDays(30));
        var client = new ImClientSession(ca.Anchors(), new FixedTime(Now));
        var knock = client.Knock(new byte[32]);
        Assert.Equal(ImClientError.OutOfOrder, Assert.Throws<ImClientException>(() => client.Knock(new byte[32])).Error);
        Assert.False(client.GateChecked);
        client.CheckGate(ImFrames.Accept(knock, new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], gate));
        Assert.True(client.GateChecked);
    }

    [Fact]
    public void An_accept_whose_gate_chain_is_not_a_certificate_is_malformed_to_the_client()
    {
        var gate = ca.Issue(TrustRole.MilkyWayGate, "milky-way", Now.AddDays(-1), Now.AddDays(30));
        var client = new ImClientSession(ca.Anchors(), new FixedTime(Now));
        var knock = client.Knock(new byte[32]);
        var accept = ImFrames.Accept(knock, new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], gate);
        var parsed = ImFrames.ParseAccept(accept);
        // Rebuild the frame with a gate chain of bytes that are not a certificate: the client refuses it as malformed, not as untrusted.
        var broken = new ImWriter().U8((byte)ImFrameType.Accept).Fixed(parsed.Challenge, ImFrames.ChallengeLength).Fixed(parsed.SessionId, ImFrames.SessionIdLength)
            .List16([[1, 2, 3]], ImLimits.ChainMax, ImLimits.CertificateMax).Fixed(parsed.Signature, ImLimits.SignatureLength).ToArray();
        Assert.Equal(ImClientError.Malformed, Assert.Throws<ImClientException>(() => client.CheckGate(broken)).Error);
    }

    [Fact]
    public void A_door_reply_whose_body_is_cut_is_malformed_to_the_client() =>
        Assert.Equal(ImClientError.Malformed, Assert.Throws<ImClientException>(() => ImClientSession.ReadDoorReply(ImFrames.DoorReply("abc"u8)[..^1])).Error);

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { (byte)ImFrameType.Refused })]
    public void A_door_reply_that_is_empty_or_refused_is_a_refusal(byte[] frame) =>
        Assert.Equal(ImClientError.DoorRefused, Assert.Throws<ImClientException>(() => ImClientSession.ReadDoorReply(frame)).Error);

    // ---- stream framing ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stream_cut_inside_the_length_prefix_or_the_frame_is_truncated_not_an_exception()
    {
        Assert.Equal(ImStreamOutcome.Closed, (await ImStreamFraming.ReadAsync(new MemoryStream([]), 16, CancellationToken.None)).Outcome);
        Assert.Equal(ImStreamOutcome.Truncated, (await ImStreamFraming.ReadAsync(new MemoryStream([0, 0, 0]), 16, CancellationToken.None)).Outcome);
        Assert.Equal(ImStreamOutcome.Truncated, (await ImStreamFraming.ReadAsync(new MemoryStream([0, 0, 0, 4, 1, 2]), 16, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_frame_of_exactly_the_largest_size_is_read_and_one_more_byte_is_oversize()
    {
        var wire = new MemoryStream();
        await ImStreamFraming.WriteAsync(wire, new byte[16], CancellationToken.None);
        wire.Position = 0;
        var read = await ImStreamFraming.ReadAsync(wire, 16, CancellationToken.None);
        Assert.Equal(ImStreamOutcome.Frame, read.Outcome);
        Assert.Equal(16, read.Frame!.Length);

        wire.Position = 0;
        Assert.Equal(ImStreamOutcome.Oversize, (await ImStreamFraming.ReadAsync(wire, 15, CancellationToken.None)).Outcome);
    }

    // ---- TLS options -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Both_sides_refuse_renegotiation_resumption_and_downloads()
    {
        var trust = ca.Anchors();
        using var identity = ImTlsIdentity.Of(device);
        var client = ImTls.ClientOptions(identity, trust, new FixedTime(Now));
        var server = ImTls.ServerOptions(identity, trust, new FixedTime(Now));

        Assert.False(client.AllowRenegotiation);
        Assert.False(client.AllowTlsResume);
        Assert.False(server.AllowRenegotiation);
        Assert.False(server.AllowTlsResume);
        Assert.Equal(System.Security.Authentication.SslProtocols.Tls13, client.EnabledSslProtocols);
        Assert.Equal(System.Security.Authentication.SslProtocols.Tls13, server.EnabledSslProtocols);
        Assert.True(server.ClientCertificateRequired);
        Assert.Equal(EncryptionPolicy.RequireEncryption, client.EncryptionPolicy);
        foreach (var policy in new[] { client.CertificateChainPolicy!, server.CertificateChainPolicy! })
        {
            Assert.True(policy.DisableCertificateDownloads);
            Assert.Equal(System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
            Assert.Equal(System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck, policy.RevocationMode);
        }
    }

    [Fact]
    public void A_TLS_identity_needs_a_certificate_joined_to_its_key()
    {
        using var bare = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(device.Certificate.RawData);
        Assert.False(bare.HasPrivateKey);
        Assert.Throws<ArgumentException>(() => ImTlsIdentity.Bound(bare, []));
        Assert.Throws<ArgumentException>(() => ImTlsIdentity.Of(new KeylessSigner(bare, device)));
    }

    [Fact]
    public async Task An_empty_frame_is_never_written()
    {
        var wire = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => ImStreamFraming.WriteAsync(wire, Array.Empty<byte>(), CancellationToken.None));
        Assert.Equal(0, wire.Length);
    }

    [Fact]
    public void A_token_that_expires_before_it_is_valid_is_never_issued()
    {
        var claims = new ImTokenClaims("d-0001", EnrolledRole.Laptop, ImKeys.SubjectPublicKeyInfo(device.Certificate), [ImKinds.SessionPermission], Now, Now.AddTicks(-1));
        Assert.Throws<ArgumentException>(() => ImTokenCodec.Issue(claims, signer, Now));
        Assert.NotNull(ImTokenCodec.Issue(claims with { ExpiresAt = Now }, signer, Now)); // a token valid for an instant is allowed
    }

    [Fact]
    public void A_certificate_check_that_throws_inside_the_platform_fails_closed_as_malformed()
    {
        Assert.Equal(ChainRefusal.Malformed, ImTls.Check(new ThrowingTrust(), device.Certificate, null, TrustRole.Device, Now).Refusal);
        Assert.Equal(ChainRefusal.Malformed, ImTls.Check(ca.Anchors(), null, null, TrustRole.Device, Now).Refusal);
        Assert.True(ImTls.Check(ca.Anchors(), device.Certificate, null, TrustRole.Device, Now).Ok == false); // without its intermediate
    }

    sealed class ThrowingTrust : ITrustAnchors
    {
        public ChainVerdict Validate(System.Security.Cryptography.X509Certificates.X509Certificate2 leaf, IReadOnlyList<System.Security.Cryptography.X509Certificates.X509Certificate2> intermediates, TrustRole role, DateTimeOffset at) =>
            throw new CryptographicException("the platform refused");
    }

    sealed class KeylessSigner(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, IImSigner inner) : IImSigner
    {
        public System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate => certificate;
        public IReadOnlyList<System.Security.Cryptography.X509Certificates.X509Certificate2> Chain => inner.Chain;
        public string KeyId => inner.KeyId;
        public byte[] Sign(ReadOnlySpan<byte> data) => inner.Sign(data);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>A token whose body is exactly <see cref="ImLimits.TokenMax"/> bytes: the signer chain is padded with bytes Parse never reads as a certificate.</summary>
    byte[] LargestToken()
    {
        var key = ImKeys.SubjectPublicKeyInfo(device.Certificate);
        byte[] Body(int padding) => new ImWriter()
            .Raw("IMT"u8).U8(0)
            .Fixed(new byte[16], 16)
            .Id8("d-0001", ImAlphabet.Subject, ImLimits.SubjectMax)
            .Id8("laptop", ImAlphabet.Name, ImLimits.RoleMax)
            .Fixed(ImKeys.KeyIdBytes(key), 32)
            .Bytes16(key, ImLimits.KeyMax)
            .U8(1).Id8(ImKinds.HeartbeatPermission, ImAlphabet.Name, ImLimits.NameMax)
            .I64(ImTime.ToWire(Now)).I64(ImTime.ToWire(Now)).I64(ImTime.ToWire(Now.AddHours(8)))
            .Fixed(new byte[16], 16)
            .List16([new byte[ImLimits.CertificateMax], new byte[ImLimits.CertificateMax], new byte[ImLimits.CertificateMax], new byte[padding]], ImLimits.ChainMax, ImLimits.CertificateMax)
            .ToArray();
        var body = Body(0);
        body = Body(ImLimits.TokenMax - body.Length);
        Assert.Equal(ImLimits.TokenMax, body.Length);
        return new ImWriter().Bytes32(body, ImLimits.TokenMax).Fixed(new byte[64], ImLimits.SignatureLength).ToArray();
    }

    static void Malformed(Action parse) => Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(parse).Error);

    sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
