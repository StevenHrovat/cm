using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>The TPM 2.0 constants Doppler reads (TCG TPM 2.0 Library, Part 2: Structures). Only what attestation needs.</summary>
public static class TpmAlg
{
    public const ushort Rsa = 0x0001;
    public const ushort Sha1 = 0x0004;
    public const ushort Sha256 = 0x000B;
    public const ushort Aes = 0x0006;
    public const ushort Null = 0x0010;
    public const ushort RsaSsa = 0x0014;
    public const ushort RsaPss = 0x0016;
    public const ushort EcDsa = 0x0018;
    public const ushort EcDaa = 0x001A;
    public const ushort Ecc = 0x0023;
    public const ushort Cfb = 0x0043;
    public const ushort EccNistP256 = 0x0003;

    /// <summary>TPM_GENERATED_VALUE: the magic that only the TPM puts at the start of an attestation it signs.</summary>
    public const uint Generated = 0xFF544347;
    public const ushort AttestCertify = 0x8017;
    public const ushort AttestQuote = 0x8018;

    public static int DigestSize(ushort alg) => alg switch
    {
        Sha1 => 20,
        Sha256 => 32,
        _ => throw new TpmFormatException("unsupported hash algorithm"),
    };

    public static HashAlgorithmName HashName(ushort alg) => alg switch
    {
        Sha1 => HashAlgorithmName.SHA1,
        Sha256 => HashAlgorithmName.SHA256,
        _ => throw new TpmFormatException("unsupported hash algorithm"),
    };

    public static byte[] Hash(ushort alg, ReadOnlySpan<byte> data) => alg switch
    {
#pragma warning disable CA5350 // SHA-1 is read only to replay a SHA-1 PCR bank when a TPM quotes that bank; Doppler's policy refuses it (see TpmEvidence).
        Sha1 => SHA1.HashData(data),
#pragma warning restore CA5350
        Sha256 => SHA256.HashData(data),
        _ => throw new TpmFormatException("unsupported hash algorithm"),
    };
}

/// <summary>TPMA_OBJECT bits (Part 2, 8.3).</summary>
[Flags]
public enum TpmObjectAttributes : uint
{
    None = 0,
    FixedTpm = 1u << 1,
    StClear = 1u << 2,
    FixedParent = 1u << 4,
    SensitiveDataOrigin = 1u << 5,
    UserWithAuth = 1u << 6,
    AdminWithPolicy = 1u << 7,
    NoDa = 1u << 10,
    EncryptedDuplication = 1u << 11,
    Restricted = 1u << 16,
    Decrypt = 1u << 17,
    Sign = 1u << 18,
}

/// <summary>A TPM structure did not parse. The message names the field kind, never the bytes.</summary>
public sealed class TpmFormatException(string what) : Exception(what);

/// <summary>Big-endian, bounds-checked reader of TPM marshalled structures; <see cref="End"/> refuses trailing bytes.</summary>
public sealed class TpmReader(ReadOnlyMemory<byte> data)
{
    int position;

    public int Position => position;

    public bool AtEnd => position == data.Length;

    public byte U8() => Take(1).Span[0];

    public ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2).Span);

    public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4).Span);

    public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8).Span);

    /// <summary>A TPM2B: a <c>u16</c> size then that many bytes, at most <paramref name="max"/>.</summary>
    public byte[] Sized(int max)
    {
        var size = U16();
        if (size > max) throw new TpmFormatException("a sized field is over its limit");
        return Take(size).ToArray();
    }

    public byte[] Fixed(int length) => Take(length).ToArray();

    public ReadOnlyMemory<byte> Slice(int from, int to) => data[from..to];

    public void End()
    {
        if (position != data.Length) throw new TpmFormatException("trailing bytes");
    }

    ReadOnlyMemory<byte> Take(int length)
    {
        if (length < 0 || length > data.Length - position) throw new TpmFormatException("truncated");
        var slice = data.Slice(position, length);
        position += length;
        return slice;
    }
}

/// <summary>Big-endian writer of TPM marshalled structures (the software TPM and MakeCredential use it).</summary>
public sealed class TpmWriter
{
    readonly MemoryStream stream = new();

    public TpmWriter U8(byte v)
    {
        stream.WriteByte(v);
        return this;
    }

    public TpmWriter U16(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        stream.Write(b);
        return this;
    }

    public TpmWriter U32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        stream.Write(b);
        return this;
    }

    public TpmWriter U64(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, v);
        stream.Write(b);
        return this;
    }

    public TpmWriter Sized(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > ushort.MaxValue) throw new ArgumentException("too long for a TPM2B", nameof(bytes));
        U16((ushort)bytes.Length);
        stream.Write(bytes);
        return this;
    }

    public TpmWriter Raw(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        return this;
    }

    public byte[] ToArray() => stream.ToArray();
}

/// <summary>
/// A TPMT_PUBLIC (Part 2, 12.2.4): the public area of a TPM key, RSA or ECC. <see cref="Encoded"/> is exactly the bytes that were
/// parsed, so <see cref="Name"/> (nameAlg then the hash of those bytes) is the name the TPM itself computes for the key.
/// </summary>
public sealed class TpmPublic
{
    const int MaxField = 1024;

    TpmPublic(byte[] encoded, ushort type, ushort nameAlg, TpmObjectAttributes attributes, ushort symmetric, ushort scheme, ushort schemeHash,
        int rsaBits, uint rsaExponent, byte[] rsaModulus, ushort curve, byte[] eccX, byte[] eccY)
    {
        Encoded = encoded;
        Type = type;
        NameAlg = nameAlg;
        Attributes = attributes;
        Symmetric = symmetric;
        Scheme = scheme;
        SchemeHash = schemeHash;
        RsaBits = rsaBits;
        RsaExponent = rsaExponent;
        RsaModulus = rsaModulus;
        Curve = curve;
        EccX = eccX;
        EccY = eccY;
    }

    public byte[] Encoded { get; }
    public ushort Type { get; }
    public ushort NameAlg { get; }
    public TpmObjectAttributes Attributes { get; }
    /// <summary>The symmetric algorithm of a storage key, <see cref="TpmAlg.Null"/> for a signing key.</summary>
    public ushort Symmetric { get; }
    public ushort Scheme { get; }
    public ushort SchemeHash { get; }
    public int RsaBits { get; }
    public uint RsaExponent { get; }
    public byte[] RsaModulus { get; }
    public ushort Curve { get; }
    public byte[] EccX { get; }
    public byte[] EccY { get; }

    public bool Has(TpmObjectAttributes bits) => (Attributes & bits) == bits;

    /// <summary>The TPM name of the key: nameAlg (u16) then the hash of the public area with that algorithm.</summary>
    public byte[] Name => [.. new TpmWriter().U16(NameAlg).ToArray(), .. TpmAlg.Hash(NameAlg, Encoded)];

    public static TpmPublic Parse(ReadOnlyMemory<byte> encoded)
    {
        var r = new TpmReader(encoded);
        var type = r.U16();
        var nameAlg = r.U16();
        TpmAlg.DigestSize(nameAlg);
        var attributes = (TpmObjectAttributes)r.U32();
        r.Sized(64); // authPolicy
        var symmetric = r.U16();
        if (symmetric != TpmAlg.Null)
        {
            r.U16(); // key bits
            r.U16(); // mode
        }
        var scheme = r.U16();
        ushort schemeHash = 0;
        TpmPublic result;
        switch (type)
        {
            case TpmAlg.Rsa:
                if (scheme != TpmAlg.Null) schemeHash = r.U16();
                var bits = r.U16();
                var exponent = r.U32();
                var modulus = r.Sized(MaxField);
                r.End();
                result = new(encoded.ToArray(), type, nameAlg, attributes, symmetric, scheme, schemeHash, bits, exponent == 0 ? 65537u : exponent, modulus, 0, [], []);
                break;
            case TpmAlg.Ecc:
                if (scheme == TpmAlg.EcDaa) throw new TpmFormatException("ECDAA keys are not accepted");
                if (scheme != TpmAlg.Null) schemeHash = r.U16();
                var curve = r.U16();
                var kdf = r.U16();
                if (kdf != TpmAlg.Null) r.U16();
                var x = r.Sized(MaxField);
                var y = r.Sized(MaxField);
                r.End();
                result = new(encoded.ToArray(), type, nameAlg, attributes, symmetric, scheme, schemeHash, 0, 0, [], curve, x, y);
                break;
            default:
                throw new TpmFormatException("unsupported key type");
        }
        return result;
    }

    /// <summary>A TPM2B_PUBLIC (u16 size, then the TPMT_PUBLIC), as tpm2-tools and the platform crypto provider's key blobs carry it.</summary>
    public static TpmPublic ParseSized(ReadOnlyMemory<byte> sized)
    {
        var r = new TpmReader(sized);
        var inner = r.Sized(4096);
        r.End();
        return Parse(inner);
    }

    /// <summary>The DER SubjectPublicKeyInfo of an ECC P-256 key, to compare with a certificate request and to name the device key.</summary>
    public byte[] EccP256SubjectPublicKeyInfo()
    {
        if (Type != TpmAlg.Ecc || Curve != TpmAlg.EccNistP256 || EccX.Length > 32 || EccY.Length > 32) throw new TpmFormatException("not an ECC P-256 key");
        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = Pad(EccX, 32), Y = Pad(EccY, 32) } });
        return key.ExportSubjectPublicKeyInfo();
    }

    /// <summary>The RSA public key, to verify the attestation key's signatures.</summary>
    public RSA RsaKey()
    {
        if (Type != TpmAlg.Rsa) throw new TpmFormatException("not an RSA key");
        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = RsaModulus, Exponent = new BigInteger(RsaExponent).ToByteArray(isUnsigned: true, isBigEndian: true) });
        return rsa;
    }

    // ---- building public areas (the software TPM) ---------------------------------------------------------------------

    public static byte[] EncodeRsa(TpmObjectAttributes attributes, ushort scheme, ushort schemeHash, byte[] modulus, ushort symmetric = TpmAlg.Null, ushort nameAlg = TpmAlg.Sha256)
    {
        var w = new TpmWriter().U16(TpmAlg.Rsa).U16(nameAlg).U32((uint)attributes).Sized([]);
        if (symmetric == TpmAlg.Null) w.U16(TpmAlg.Null);
        else w.U16(symmetric).U16(128).U16(TpmAlg.Cfb);
        w.U16(scheme);
        if (scheme != TpmAlg.Null) w.U16(schemeHash);
        return w.U16((ushort)(modulus.Length * 8)).U32(0).Sized(modulus).ToArray();
    }

    public static byte[] EncodeEccP256(TpmObjectAttributes attributes, byte[] x, byte[] y, ushort scheme = TpmAlg.Null, ushort schemeHash = 0)
    {
        var w = new TpmWriter().U16(TpmAlg.Ecc).U16(TpmAlg.Sha256).U32((uint)attributes).Sized([]).U16(TpmAlg.Null).U16(scheme);
        if (scheme != TpmAlg.Null) w.U16(schemeHash);
        return w.U16(TpmAlg.EccNistP256).U16(TpmAlg.Null).Sized(x).Sized(y).ToArray();
    }

    static byte[] Pad(byte[] value, int length)
    {
        if (value.Length == length) return value;
        var padded = new byte[length];
        value.CopyTo(padded, length - value.Length);
        return padded;
    }
}

/// <summary>What a TPMS_ATTEST says (Part 2, 10.12.12), for the two kinds Doppler reads: certify and quote.</summary>
public sealed record TpmAttest(
    ushort Type,
    byte[] QualifiedSigner,
    byte[] ExtraData,
    ulong Clock,
    uint ResetCount,
    uint RestartCount,
    bool Safe,
    ulong FirmwareVersion,
    byte[] CertifiedName,
    byte[] CertifiedQualifiedName,
    IReadOnlyList<(ushort Hash, byte[] Select)> PcrSelection,
    byte[] PcrDigest)
{
    public static TpmAttest Parse(ReadOnlyMemory<byte> encoded)
    {
        var r = new TpmReader(encoded);
        if (r.U32() != TpmAlg.Generated) throw new TpmFormatException("not generated by a TPM");
        var type = r.U16();
        var signer = r.Sized(68);
        var extra = r.Sized(64);
        var clock = r.U64();
        var reset = r.U32();
        var restart = r.U32();
        var safe = r.U8();
        if (safe > 1) throw new TpmFormatException("bad clock info");
        var firmware = r.U64();
        TpmAttest result;
        switch (type)
        {
            case TpmAlg.AttestCertify:
                result = new(type, signer, extra, clock, reset, restart, safe == 1, firmware, r.Sized(68), r.Sized(68), [], []);
                break;
            case TpmAlg.AttestQuote:
                var count = r.U32();
                if (count is 0 or > 4) throw new TpmFormatException("bad PCR selection");
                var selection = new List<(ushort, byte[])>();
                for (var i = 0; i < count; i++)
                {
                    var hash = r.U16();
                    var size = r.U8();
                    if (size is 0 or > 8) throw new TpmFormatException("bad PCR selection");
                    selection.Add((hash, r.Fixed(size)));
                }
                result = new(type, signer, extra, clock, reset, restart, safe == 1, firmware, [], [], selection, r.Sized(64));
                break;
            default:
                throw new TpmFormatException("unsupported attestation type");
        }
        r.End();
        return result;
    }

    // ---- building attestations (the software TPM) ---------------------------------------------------------------------

    public static byte[] EncodeCertify(byte[] signerName, byte[] extraData, byte[] certifiedName, ulong clock = 1000, uint resetCount = 1) =>
        Header(TpmAlg.AttestCertify, signerName, extraData, clock, resetCount).Sized(certifiedName).Sized(certifiedName).ToArray();

    public static byte[] EncodeQuote(byte[] signerName, byte[] extraData, ushort bank, int pcr, byte[] pcrDigest, ulong clock = 1000, uint resetCount = 1)
    {
        var select = new byte[3];
        select[pcr / 8] |= (byte)(1 << (pcr % 8));
        return Header(TpmAlg.AttestQuote, signerName, extraData, clock, resetCount).U32(1).U16(bank).U8(3).Raw(select).Sized(pcrDigest).ToArray();
    }

    static TpmWriter Header(ushort type, byte[] signerName, byte[] extraData, ulong clock, uint resetCount) =>
        new TpmWriter().U32(TpmAlg.Generated).U16(type).Sized(signerName).Sized(extraData).U64(clock).U32(resetCount).U32(0).U8(1).U64(0x2000_0001_0000_0000);
}

/// <summary>A TPMT_SIGNATURE (Part 2, 11.3.4) by an RSA attestation key, and its check.</summary>
public sealed record TpmSignature(ushort Algorithm, ushort Hash, byte[] Value)
{
    public static TpmSignature Parse(ReadOnlyMemory<byte> encoded)
    {
        var r = new TpmReader(encoded);
        var alg = r.U16();
        if (alg is not (TpmAlg.RsaSsa or TpmAlg.RsaPss)) throw new TpmFormatException("unsupported signature algorithm");
        var hash = r.U16();
        var value = r.Sized(512);
        r.End();
        return new(alg, hash, value);
    }

    public byte[] Encode() => new TpmWriter().U16(Algorithm).U16(Hash).Sized(Value).ToArray();

    /// <summary>
    /// True when this is a signature by <paramref name="key"/> over <paramref name="signed"/> with SHA-256, or SHA-1 when
    /// <paramref name="allowSha1"/> (Windows' platform crypto provider signs with SHA-1). When the key names a scheme, the signature
    /// must use exactly that scheme and hash.
    /// </summary>
    public bool Verifies(TpmPublic key, ReadOnlySpan<byte> signed, bool allowSha1 = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Type != TpmAlg.Rsa) return false;
        if (Hash != TpmAlg.Sha256 && !(allowSha1 && Hash == TpmAlg.Sha1)) return false;
        if (key.Scheme != TpmAlg.Null && (key.Scheme != Algorithm || key.SchemeHash != Hash)) return false;
        using var rsa = key.RsaKey();
        var padding = Algorithm == TpmAlg.RsaSsa ? RSASignaturePadding.Pkcs1 : RSASignaturePadding.Pss;
        try { return rsa.VerifyData(signed, Value, TpmAlg.HashName(Hash), padding); }
        catch (CryptographicException) { return false; }
    }
}
