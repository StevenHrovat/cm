using System.Security.Cryptography;
using Im;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests;

/// <summary>The encoding of IM version 0 (DMP chapter 04, "Encoding", "The token", "The envelope"): strict, canonical, one algorithm.</summary>
[Trait("Category", "im")]
public sealed class ImCodecTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    readonly SandboxAuthority ca = new("sandbox", Now.AddDays(-30));
    readonly IImSigner signer;
    readonly IImSigner device;

    public ImCodecTests()
    {
        signer = ca.Issue(TrustRole.ImTokenSigner, "doppler-token-signer", Now.AddDays(-1), Now.AddDays(30));
        device = ca.Issue(TrustRole.Device, "d-0001", Now.AddDays(-1), Now.AddDays(30));
    }

    public void Dispose() => ca.Dispose();

    ImTokenClaims Claims(IReadOnlyCollection<string>? permissions = null, byte[]? key = null) =>
        new("d-0001", EnrolledRole.Laptop, key ?? ImKeys.SubjectPublicKeyInfo(device.Certificate), permissions ?? [ImKinds.HeartbeatPermission, ImKinds.SessionPermission], Now, Now.AddHours(8));

    [Fact]
    public void A_token_round_trips_with_every_claim()
    {
        var token = ImTokenCodec.Parse(ImTokenCodec.Issue(Claims(), signer, Now));
        Assert.Equal("d-0001", token.Subject);
        Assert.Equal(EnrolledRole.Laptop, token.Role);
        Assert.Equal(device.KeyId, token.SubjectKeyIdHex);
        Assert.Equal([ImKinds.HeartbeatPermission, ImKinds.SessionPermission], token.Permissions); // ascending ordinal
        Assert.Equal(ImTime.ToWire(Now), token.NotBefore);
        Assert.Equal(ImTime.ToWire(Now.AddHours(8)), token.ExpiresAt);
        Assert.Equal(2, token.SignerChain.Count);
        Assert.Equal(signer.Certificate.RawData, token.SignerChain[0]);
        Assert.True(ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(signer.Certificate), ImTags.Input(ImTags.Token, token.Body), token.Signature));
    }

    [Fact]
    public void I6_trailing_or_missing_bytes_make_a_token_malformed()
    {
        var encoded = ImTokenCodec.Issue(Claims(), signer, Now);
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImTokenCodec.Parse(encoded.Concat(new byte[] { 0 }).ToArray())).Error);
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImTokenCodec.Parse(encoded.AsMemory(0, encoded.Length - 1))).Error);
    }

    [Fact]
    public void An_unknown_version_is_named_as_such()
    {
        var encoded = ImTokenCodec.Issue(Claims(), signer, Now);
        encoded[4 + 3] = 1; // the version byte after the u32 body length and the magic
        Assert.Equal(ImFormatError.UnknownVersion, Assert.Throws<ImFormatException>(() => ImTokenCodec.Parse(encoded)).Error);
    }

    [Fact]
    public void I4_a_subject_key_on_another_curve_cannot_be_put_in_a_token()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var rsa = RSA.Create(2048);
        Assert.False(ImKeys.IsCanonicalP256(p384.ExportSubjectPublicKeyInfo()));
        Assert.False(ImKeys.IsCanonicalP256(rsa.ExportSubjectPublicKeyInfo()));
        Assert.Throws<ArgumentException>(() => ImTokenCodec.Issue(Claims(key: p384.ExportSubjectPublicKeyInfo()), signer, Now));
    }

    [Fact]
    public void I4_a_signature_of_another_length_never_verifies()
    {
        var spki = ImKeys.SubjectPublicKeyInfo(device.Certificate);
        var data = new byte[] { 1, 2, 3 };
        var good = ImKeys.Sign(device, data);
        Assert.True(ImKeys.Verify(spki, data, good));
        Assert.False(ImKeys.Verify(spki, data, good.AsSpan(0, 63)));
        Assert.False(ImKeys.Verify(spki, data, good.Concat(new byte[] { 0 }).ToArray()));
    }

    [Fact]
    public void A_permission_listed_twice_or_none_is_refused_when_issuing()
    {
        Assert.Throws<ArgumentException>(() => ImTokenCodec.Issue(Claims([ImKinds.HeartbeatPermission, ImKinds.HeartbeatPermission]), signer, Now));
        Assert.Throws<ArgumentException>(() => ImTokenCodec.Issue(Claims([]), signer, Now));
    }

    [Theory]
    [InlineData("D-0001")]       // upper case
    [InlineData("d_0001")]       // underscore
    [InlineData("d-0001.host")]  // a dot is a name character, not a subject character
    [InlineData("d-а001")]   // a Cyrillic look-alike
    [InlineData("")]
    public void A_subject_outside_its_alphabet_cannot_be_written(string subject)
    {
        Assert.Throws<ArgumentException>(() => ImTokenCodec.Issue(Claims() with { Subject = subject }, signer, Now));
    }

    [Fact]
    public void An_envelope_round_trips_and_its_signature_covers_the_token()
    {
        var token = ImTokenCodec.Issue(Claims(), signer, Now);
        var payload = new ImHeartbeat(42, ImHeartbeatStatus.Degraded, "0.1.0").Encode();
        var envelope = ImEnvelopeCodec.Parse(ImEnvelopeCodec.Seal(ImKinds.Heartbeat, payload, token, device, Now, 4096), 4096);
        Assert.Equal(ImKinds.Heartbeat, envelope.Kind);
        Assert.Equal(token, envelope.Token);
        Assert.Equal(ImTime.ToWire(Now), envelope.SentAt);
        Assert.Equal(new ImHeartbeat(42, ImHeartbeatStatus.Degraded, "0.1.0"), ImHeartbeat.Parse(envelope.Payload));
        Assert.True(ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(device.Certificate), ImTags.Input(ImTags.Message, envelope.Body), envelope.Signature));
    }

    [Fact]
    public void A_payload_over_the_limit_is_refused_both_ways()
    {
        var token = ImTokenCodec.Issue(Claims(), signer, Now);
        Assert.Throws<ArgumentException>(() => ImEnvelopeCodec.Seal(ImKinds.Heartbeat, new byte[100], token, device, Now, 99));
        var sealedEnvelope = ImEnvelopeCodec.Seal(ImKinds.Heartbeat, new byte[100], token, device, Now, 100);
        Assert.Throws<ImFormatException>(() => ImEnvelopeCodec.Parse(sealedEnvelope, 99));
    }

    [Fact]
    public void A_knock_is_exactly_its_length_and_its_mac_depends_on_the_secret()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var knock = ImFrames.Knock(secret, Now);
        Assert.Equal(ImFrames.KnockLength, knock.Length);
        var parsed = ImFrames.ParseKnock(knock);
        Assert.Equal(parsed.Mac, ImFrames.KnockMac(secret, parsed.ClientNonce, parsed.SentAt));
        Assert.NotEqual(parsed.Mac, ImFrames.KnockMac(new byte[32], parsed.ClientNonce, parsed.SentAt));
        Assert.Throws<ImFormatException>(() => ImFrames.ParseKnock(knock.Concat(new byte[] { 0 }).ToArray()));
    }

    [Fact]
    public void Domain_tags_keep_a_token_signature_from_verifying_as_a_message_signature()
    {
        var body = new byte[] { 9, 9, 9 };
        var asToken = ImKeys.Sign(signer, ImTags.Input(ImTags.Token, body));
        Assert.False(ImKeys.Verify(ImKeys.SubjectPublicKeyInfo(signer.Certificate), ImTags.Input(ImTags.Message, body), asToken));
    }

    [Fact]
    public void Every_role_has_one_wire_spelling_and_back()
    {
        foreach (var role in Enum.GetValues<EnrolledRole>())
            Assert.Equal(role, ImRoles.FromWire(ImRoles.ToWire(role)));
        Assert.Throws<ImFormatException>(() => ImRoles.FromWire("Laptop"));
    }

    [Fact]
    public void The_kind_table_names_a_permission_for_every_kind()
    {
        Assert.Equal(ImKinds.HeartbeatPermission, ImKinds.Required[ImKinds.Heartbeat]);
        Assert.All(ImKinds.Required, p => Assert.True(ImText.IsValid(p.Key, ImAlphabet.Name, ImLimits.NameMax) && ImText.IsValid(p.Value, ImAlphabet.Name, ImLimits.NameMax)));
    }
}
