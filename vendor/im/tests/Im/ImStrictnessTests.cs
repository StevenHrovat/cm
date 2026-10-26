using Im;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests;

/// <summary>
/// Strict parsing, one check at a time (DMP chapter 04, "Encoding"; invariant I6): each input breaks exactly one rule of an otherwise
/// valid spelling, so removing that rule's check lets it through and fails the test. Found missing by the mutation run of Supernova
/// level 3 (DMP chapter 06). A token is built by hand here because <see cref="ImTokenCodec.Issue"/> refuses to write these spellings.
/// </summary>
[Trait("Category", "im")]
public sealed class ImStrictnessTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxAuthority ca = new("sandbox", Now.AddDays(-30));
    readonly IImSigner signer;
    readonly IImSigner device;

    public ImStrictnessTests()
    {
        signer = ca.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Now.AddDays(-1), Now.AddDays(30));
        device = ca.Issue(TrustRole.Device, "d-0001", Now.AddDays(-1), Now.AddDays(30));
    }

    public void Dispose() => ca.Dispose();

    /// <summary>A token in the layout of DMP 04 "The token", with each field open to a change. The signature is not checked by Parse.</summary>
    byte[] Token(string[]? permissions = null, int? count = null, byte[]? subjectKey = null, byte[]? subjectKeyId = null, IReadOnlyList<byte[]>? chain = null)
    {
        var key = subjectKey ?? ImKeys.SubjectPublicKeyInfo(device.Certificate);
        var names = permissions ?? [ImKinds.HeartbeatPermission, ImKinds.SessionPermission];
        var writer = new ImWriter()
            .Raw("IMT"u8).U8(0)
            .Fixed(new byte[16], 16)
            .Id8("d-0001", ImAlphabet.Subject, ImLimits.SubjectMax)
            .Id8("laptop", ImAlphabet.Name, ImLimits.RoleMax)
            .Fixed(subjectKeyId ?? ImKeys.KeyIdBytes(key), 32)
            .Bytes16(key, ImLimits.KeyMax)
            .U8((byte)(count ?? names.Length));
        foreach (var name in names) writer.Id8(name, ImAlphabet.Name, ImLimits.NameMax);
        var body = writer
            .I64(ImTime.ToWire(Now)).I64(ImTime.ToWire(Now)).I64(ImTime.ToWire(Now.AddHours(8)))
            .Fixed(new byte[16], 16)
            .List16(chain ?? ImChains.Of(signer), ImLimits.ChainMax, ImLimits.CertificateMax)
            .ToArray();
        return new ImWriter().Bytes32(body, ImLimits.TokenMax).Fixed(new byte[64], ImLimits.SignatureLength).ToArray();
    }

    [Fact]
    public void The_hand_built_token_parses_so_each_change_below_is_the_only_fault()
    {
        var token = ImTokenCodec.Parse(Token());
        Assert.Equal([ImKinds.HeartbeatPermission, ImKinds.SessionPermission], token.Permissions);
    }

    [Fact]
    public void A_token_with_no_permission_is_malformed() => AssertMalformed(() => ImTokenCodec.Parse(Token(permissions: [])));

    [Fact]
    public void A_token_with_more_permissions_than_the_limit_is_malformed()
    {
        var names = Enumerable.Range(0, ImLimits.PermissionsMax + 1).Select(i => $"p.{i:D2}").ToArray();
        AssertMalformed(() => ImTokenCodec.Parse(Token(permissions: names)));
    }

    [Fact]
    public void A_token_with_permissions_out_of_order_is_malformed() =>
        AssertMalformed(() => ImTokenCodec.Parse(Token(permissions: [ImKinds.SessionPermission, ImKinds.HeartbeatPermission])));

    [Fact]
    public void A_token_with_a_permission_twice_is_malformed() =>
        AssertMalformed(() => ImTokenCodec.Parse(Token(permissions: [ImKinds.HeartbeatPermission, ImKinds.HeartbeatPermission])));

    [Fact]
    public void A_token_whose_subject_key_is_not_P256_is_malformed()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        var key = p384.ExportSubjectPublicKeyInfo();
        AssertMalformed(() => ImTokenCodec.Parse(Token(subjectKey: key)));
    }

    [Fact]
    public void A_token_whose_key_id_is_not_the_hash_of_its_key_is_malformed() =>
        AssertMalformed(() => ImTokenCodec.Parse(Token(subjectKeyId: new byte[32])));

    [Fact]
    public void A_token_with_an_empty_signer_chain_is_malformed()
    {
        // List16 refuses a count of 0; the writer refuses to write one, so the count byte is set by hand.
        var token = Token();
        var chainCountAt = 4 + TokenBodyChainOffset(token);
        token[chainCountAt] = 0;
        AssertMalformed(() => ImTokenCodec.Parse(token));
    }

    [Fact]
    public void A_field_longer_than_its_limit_is_malformed_even_when_the_bytes_are_there()
    {
        var bytes = new ImWriter().U16(5).Raw(new byte[5]).ToArray();
        AssertMalformed(() => new ImReader(bytes).Bytes16(max: 4));
        Assert.Equal(5, new ImReader(bytes).Bytes16(max: 5).Length);
    }

    [Fact]
    public void An_empty_id8_is_malformed() => AssertMalformed(() => new ImReader(new byte[] { 0 }).Id8(ImAlphabet.Name, 8));

    [Fact]
    public void A_knock_one_byte_longer_or_shorter_is_malformed()
    {
        var knock = ImFrames.Knock(new byte[32], Now);
        Assert.Equal(ImFrames.KnockLength, knock.Length);
        AssertMalformed(() => ImFrames.ParseKnock(knock.Concat(new byte[] { 0 }).ToArray()));
        AssertMalformed(() => ImFrames.ParseKnock(knock[..^1]));
    }

    [Fact]
    public void A_frame_of_another_type_is_malformed()
    {
        var knock = ImFrames.Knock(new byte[32], Now);
        knock[0] = (byte)ImFrameType.Accept;
        AssertMalformed(() => ImFrames.ParseKnock(knock));
        var established = ImFrames.Established(new byte[ImFrames.SessionIdLength]);
        established[0] = (byte)ImFrameType.Ack;
        AssertMalformed(() => ImFrames.ParseEstablished(established));
    }

    [Fact]
    public void A_trailing_byte_after_any_frame_is_malformed()
    {
        var knock = ImFrames.Knock(new byte[32], Now);
        var accept = ImFrames.Accept(knock, new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], device);
        AssertMalformed(() => ImFrames.ParseAccept(accept.Concat(new byte[] { 0 }).ToArray()));
        AssertMalformed(() => ImFrames.ParseEstablished(ImFrames.Established(new byte[ImFrames.SessionIdLength]).Concat(new byte[] { 0 }).ToArray()));
        AssertMalformed(() => ImFrames.ParseAck(ImFrames.Ack(new byte[16]).Concat(new byte[] { 0 }).ToArray()));
        Assert.NotNull(ImFrames.ParseAccept(accept));
    }

    [Fact]
    public void Every_frame_parser_refuses_a_frame_of_another_type()
    {
        var knock = ImFrames.Knock(new byte[32], Now);
        var accept = ImFrames.Accept(knock, new byte[ImFrames.ChallengeLength], new byte[ImFrames.SessionIdLength], device);
        var response = ImFrames.Response(knock, accept, ImTokenCodec.Issue(new ImTokenClaims("d-0001", EnrolledRole.Laptop,
            ImKeys.SubjectPublicKeyInfo(device.Certificate), [ImKinds.SessionPermission], Now, Now.AddHours(1)), signer, Now), device);
        var ack = ImFrames.Ack(new byte[16]);
        var door = ImFrames.Door("x"u8);
        var doorReply = ImFrames.DoorReply("y"u8);

        Assert.NotNull(ImFrames.ParseAccept(accept));
        Assert.NotNull(ImFrames.ParseResponse(response));
        Assert.Equal(new byte[16], ImFrames.ParseAck(ack));
        Assert.Equal("x"u8.ToArray(), ImFrames.ParseDoor(door, 16));
        Assert.Equal("y"u8.ToArray(), ImFrames.ParseDoorReply(doorReply));

        AssertMalformed(() => ImFrames.ParseAccept(Retyped(accept, ImFrameType.Response)));
        AssertMalformed(() => ImFrames.ParseResponse(Retyped(response, ImFrameType.Accept)));
        AssertMalformed(() => ImFrames.ParseAck(Retyped(ack, ImFrameType.Established)));
        AssertMalformed(() => ImFrames.ParseDoor(Retyped(door, ImFrameType.DoorReply), 16));
        AssertMalformed(() => ImFrames.ParseDoorReply(Retyped(doorReply, ImFrameType.Door)));
    }

    [Fact]
    public void A_chain_that_is_not_a_certificate_is_malformed() =>
        AssertMalformed(() => ImChains.Load([ImChains.Of(signer)[0], [1, 2, 3]]));

    [Fact]
    public void A_named_256_bit_curve_other_than_P256_is_not_canonical()
    {
        System.Security.Cryptography.ECDsa brainpool;
        try { brainpool = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.brainpoolP256r1); }
        catch (PlatformNotSupportedException) { return; } // a platform without the curve cannot produce such a key either
        using (brainpool) Assert.False(ImKeys.IsCanonicalP256(brainpool.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void Bytes_that_are_not_a_key_are_not_a_canonical_P256_key_and_make_the_token_malformed()
    {
        Assert.False(ImKeys.IsCanonicalP256([1, 2, 3]));
        Assert.False(ImKeys.IsCanonicalP256(new byte[91])); // the length of a P-256 SubjectPublicKeyInfo, all zero
        var garbage = new byte[91];
        AssertMalformed(() => ImTokenCodec.Parse(Token(subjectKey: garbage)));
    }

    static byte[] Retyped(byte[] frame, ImFrameType type)
    {
        var copy = frame.ToArray();
        copy[0] = (byte)type;
        return copy;
    }

    /// <summary>The offset, inside the token body, of the signer chain's count byte: the body ends with the chain.</summary>
    int TokenBodyChainOffset(byte[] token)
    {
        var chain = ImChains.Of(signer);
        var chainLength = 1 + chain.Sum(c => 2 + c.Length);
        var bodyLength = token.Length - 4 - ImLimits.SignatureLength;
        return bodyLength - chainLength;
    }

    static void AssertMalformed(Action parse) => Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(parse).Error);
}
