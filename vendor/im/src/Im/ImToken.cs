using System.Security.Cryptography;
using Im.Trust;

namespace Im;

/// <summary>What Doppler asserts in a token (DMP chapter 04, "The token"). The codec adds the token id, the subject key id, the times and the nonce.</summary>
/// <param name="Subject">The opaque id Doppler assigned at enrolment; never a host name or a person.</param>
/// <param name="Role">The role the device is enrolled in.</param>
/// <param name="SubjectKey">The device key as SubjectPublicKeyInfo (ECDSA P-256), from its <see cref="TrustRole.Device"/> certificate.</param>
/// <param name="Permissions">The commands the device may send (<see cref="ImKinds"/>).</param>
public sealed record ImTokenClaims(string Subject, EnrolledRole Role, byte[] SubjectKey, IReadOnlyCollection<string> Permissions, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt);

/// <summary>A token as parsed from the wire. Parsing checks the shape only; whether to believe it is the verifier's job (rules 4 to 9).</summary>
public sealed class ImToken
{
    internal ImToken(byte[] encoded, byte[] body, byte[] signature, byte[] tokenId, string subject, EnrolledRole role, byte[] subjectKeyId, byte[] subjectKey,
        IReadOnlyList<string> permissions, long issuedAt, long notBefore, long expiresAt, IReadOnlyList<byte[]> signerChain)
    {
        Encoded = encoded;
        Body = body;
        Signature = signature;
        TokenId = tokenId;
        Subject = subject;
        Role = role;
        SubjectKeyId = subjectKeyId;
        SubjectKey = subjectKey;
        Permissions = permissions;
        IssuedAt = issuedAt;
        NotBefore = notBefore;
        ExpiresAt = expiresAt;
        SignerChain = signerChain;
    }

    /// <summary>The whole token as it travels (body and signature).</summary>
    public byte[] Encoded { get; }

    /// <summary>The signed body; the token signature covers <see cref="ImTags.Token"/> and these bytes.</summary>
    public byte[] Body { get; }

    public byte[] Signature { get; }

    public byte[] TokenId { get; }

    public string Subject { get; }

    public EnrolledRole Role { get; }

    public byte[] SubjectKeyId { get; }

    /// <summary>The lower-case hex form of <see cref="SubjectKeyId"/>, as Doppler's enrolment stores it.</summary>
    public string SubjectKeyIdHex => Convert.ToHexStringLower(SubjectKeyId);

    public byte[] SubjectKey { get; }

    public IReadOnlyList<string> Permissions { get; }

    /// <summary>Unix milliseconds.</summary>
    public long IssuedAt { get; }

    /// <summary>Unix milliseconds.</summary>
    public long NotBefore { get; }

    /// <summary>Unix milliseconds.</summary>
    public long ExpiresAt { get; }

    /// <summary>The token signing certificate first, then its intermediates, as DER.</summary>
    public IReadOnlyList<byte[]> SignerChain { get; }

    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

/// <summary>Encodes and parses tokens. Doppler issues with <see cref="Issue"/>; every reader parses with <see cref="Parse"/>.</summary>
public static class ImTokenCodec
{
    const string Magic = "IMT";
    const byte Version = 0;

    /// <summary>
    /// Builds and signs a token. <paramref name="signer"/> holds the token signing key (certificate role
    /// <see cref="TrustRole.ImTokenSigner"/>); <paramref name="issuedAt"/> is Doppler's time, passed in.
    /// </summary>
    public static byte[] Issue(ImTokenClaims claims, IImSigner signer, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(signer);
        if (!ImKeys.IsCanonicalP256(claims.SubjectKey)) throw new ArgumentException("the subject key is not a canonical ECDSA P-256 SubjectPublicKeyInfo", nameof(claims));
        if (claims.ExpiresAt < claims.NotBefore) throw new ArgumentException("the token expires before it is valid", nameof(claims));
        var permissions = claims.Permissions.Order(StringComparer.Ordinal).ToArray();
        if (permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Length) throw new ArgumentException("a permission is listed twice", nameof(claims));
        if (permissions.Length is 0 or > ImLimits.PermissionsMax) throw new ArgumentException("a token carries 1 to 16 permissions", nameof(claims));

        var writer = new ImWriter()
            .Magic(Magic, Version)
            .Fixed(RandomNumberGenerator.GetBytes(16), 16)
            .Id8(claims.Subject, ImAlphabet.Subject, ImLimits.SubjectMax)
            .Id8(ImRoles.ToWire(claims.Role), ImAlphabet.Name, ImLimits.RoleMax)
            .Fixed(ImKeys.KeyIdBytes(claims.SubjectKey), 32)
            .Bytes16(claims.SubjectKey, ImLimits.KeyMax)
            .U8((byte)permissions.Length);
        foreach (var permission in permissions) writer.Id8(permission, ImAlphabet.Name, ImLimits.NameMax);
        var body = writer
            .I64(ImTime.ToWire(issuedAt))
            .I64(ImTime.ToWire(claims.NotBefore))
            .I64(ImTime.ToWire(claims.ExpiresAt))
            .Fixed(RandomNumberGenerator.GetBytes(16), 16)
            .List16(ImChains.Of(signer), ImLimits.ChainMax, ImLimits.CertificateMax)
            .ToArray();

        var signature = ImKeys.Sign(signer, ImTags.Input(ImTags.Token, body));
        return new ImWriter().Bytes32(body, ImLimits.TokenMax).Fixed(signature, ImLimits.SignatureLength).ToArray();
    }

    /// <summary>Parses a token strictly (verify rule 3). Throws <see cref="ImFormatException"/>; never checks a signature or a time.</summary>
    public static ImToken Parse(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.Length > ImLimits.TokenMax + 4 + ImLimits.SignatureLength) throw new ImFormatException(ImFormatError.Malformed);
        var outer = new ImReader(encoded);
        var body = outer.Bytes32(ImLimits.TokenMax);
        var signature = outer.Fixed(ImLimits.SignatureLength);
        outer.End();

        var reader = new ImReader(body);
        reader.Magic(Magic, Version);
        var tokenId = reader.Fixed(16);
        var subject = reader.Id8(ImAlphabet.Subject, ImLimits.SubjectMax);
        var role = ImRoles.FromWire(reader.Id8(ImAlphabet.Name, ImLimits.RoleMax));
        var subjectKeyId = reader.Fixed(32);
        var subjectKey = reader.Bytes16(ImLimits.KeyMax);
        var count = reader.U8();
        if (count is 0 or > ImLimits.PermissionsMax) throw new ImFormatException(ImFormatError.Malformed);
        var permissions = new string[count];
        for (var i = 0; i < count; i++)
        {
            permissions[i] = reader.Id8(ImAlphabet.Name, ImLimits.NameMax);
            if (i > 0 && string.CompareOrdinal(permissions[i - 1], permissions[i]) >= 0) throw new ImFormatException(ImFormatError.Malformed);
        }
        var issuedAt = reader.I64();
        var notBefore = reader.I64();
        var expiresAt = reader.I64();
        reader.Fixed(16); // nonce: only makes two tokens with equal claims differ; nobody reads it
        var chain = reader.List16(ImLimits.ChainMax, ImLimits.CertificateMax);
        reader.End();

        if (!ImKeys.IsCanonicalP256(subjectKey)) throw new ImFormatException(ImFormatError.Malformed);
        if (!CryptographicOperations.FixedTimeEquals(ImKeys.KeyIdBytes(subjectKey), subjectKeyId)) throw new ImFormatException(ImFormatError.Malformed);

        return new ImToken(encoded.ToArray(), body, signature, tokenId, subject, role, subjectKeyId, subjectKey, permissions, issuedAt, notBefore, expiresAt, chain);
    }
}

/// <summary>The wire spelling of the enrolled roles of <c>Im.Trust</c>. One spelling per role, no aliases.</summary>
public static class ImRoles
{
    public static string ToWire(EnrolledRole role) => role switch
    {
        EnrolledRole.Runner => "runner",
        EnrolledRole.DevVm => "dev-vm",
        EnrolledRole.Test => "test",
        EnrolledRole.Build => "build",
        EnrolledRole.Laptop => "laptop",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unknown enrolled role"),
    };

    /// <summary>The role for a wire spelling; an unknown spelling is <see cref="ImFormatError.Malformed"/>.</summary>
    public static EnrolledRole FromWire(string wire) => wire switch
    {
        "runner" => EnrolledRole.Runner,
        "dev-vm" => EnrolledRole.DevVm,
        "test" => EnrolledRole.Test,
        "build" => EnrolledRole.Build,
        "laptop" => EnrolledRole.Laptop,
        _ => throw new ImFormatException(ImFormatError.Malformed),
    };
}

/// <summary>The message kinds of IM version 0 and the permission each needs (DMP chapter 04, "Message kinds and permissions").</summary>
public static class ImKinds
{
    /// <summary>The permission the handshake needs; not a message kind.</summary>
    public const string SessionPermission = "im.session.open";

    public const string Heartbeat = "im.heartbeat";
    public const string HeartbeatPermission = "im.heartbeat.send";

    /// <summary>The one table: kind to permission. A kind not in it is unknown, whatever a token lists.</summary>
    public static IReadOnlyDictionary<string, string> Required { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Heartbeat] = HeartbeatPermission,
    };
}
