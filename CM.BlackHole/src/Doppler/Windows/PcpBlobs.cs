using System.Buffers.Binary;
using System.Security.Cryptography;
using CM.BlackHole.Doppler.Tpm;

namespace CM.BlackHole.Doppler.Windows;

/// <summary>
/// The platform crypto provider's own blob formats, parsed without Windows so they are tested on every platform. Each throws
/// <see cref="TpmFormatException"/> on anything else.
/// </summary>
public static class PcpBlobs
{
    /// <summary>BCRYPT_RSAPUBLIC_MAGIC, "RSA1" little-endian.</summary>
    public const uint RsaPublicMagic = 0x31415352;

    /// <summary>
    /// The endorsement key as DER SubjectPublicKeyInfo from <c>PCP_EKPUB</c>, a BCRYPT_RSAKEY_BLOB: six little-endian u32 (magic, bit
    /// length, exponent size, modulus size, prime sizes) then the big-endian exponent and modulus.
    /// </summary>
    public static byte[] SpkiFromRsaPublicBlob(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 24) throw new TpmFormatException("short RSA public blob");
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(blob[4..]);
        var exponentSize = BinaryPrimitives.ReadUInt32LittleEndian(blob[8..]);
        var modulusSize = BinaryPrimitives.ReadUInt32LittleEndian(blob[12..]);
        if (magic != RsaPublicMagic) throw new TpmFormatException("not an RSA public blob");
        if (exponentSize is 0 or > 8 || modulusSize is < 256 or > 1024 || bits != modulusSize * 8) throw new TpmFormatException("bad RSA public blob sizes");
        if (blob.Length < 24 + exponentSize + modulusSize) throw new TpmFormatException("truncated RSA public blob");
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Exponent = blob.Slice(24, (int)exponentSize).ToArray(),
            Modulus = blob.Slice(24 + (int)exponentSize, (int)modulusSize).ToArray(),
        });
        return rsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// The attestation key's TPMT_PUBLIC from <c>PCP_TPM12_IDBINDING</c> on a TPM 2.0: TPM2B_PUBLIC, TPM2B_CREATION_DATA, TPM2B_ATTEST,
    /// TPMT_SIGNATURE (as go-attestation's decodeAKProps20 reads it). Only the public area is used.
    /// </summary>
    public static byte[] PublicFromIdBinding(ReadOnlyMemory<byte> idBinding)
    {
        if (idBinding.Length >= 4 && idBinding.Span[..4].SequenceEqual(new byte[] { 1, 1, 0, 0 })) throw new TpmFormatException("a TPM 1.2 identity binding");
        var r = new TpmReader(idBinding);
        var publicArea = r.Sized(1024);
        TpmPublic.Parse(publicArea);
        return publicArea;
    }

    /// <summary>The activation blob <c>PCP_TPM12_IDACTIVATION</c> takes on a TPM 2.0: TPM2B_ID_OBJECT then TPM2B_ENCRYPTED_SECRET, as Doppler sends them.</summary>
    public static byte[] ActivationBlob(byte[] credentialBlob, byte[] encryptedSecret)
    {
        ArgumentNullException.ThrowIfNull(credentialBlob);
        ArgumentNullException.ThrowIfNull(encryptedSecret);
        return [.. credentialBlob, .. encryptedSecret];
    }
}
