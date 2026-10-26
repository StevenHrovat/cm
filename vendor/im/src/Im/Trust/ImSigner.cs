using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Im.Trust;

/// <summary>
/// A signing key with its certificate. Doppler holds the one with role ImTokenSigner and signs IM tokens with it; an enrolled
/// device holds a Device key and signs its handshake challenge with it. The algorithm is fixed for IM version 0:
/// ECDSA on P-256 with SHA-256, signature in IEEE P1363 form (64 bytes, r then s).
/// </summary>
public interface IImSigner
{
    /// <summary>Lower-case hex SHA-256 of the certificate's SubjectPublicKeyInfo; a token names its signer by this id.</summary>
    string KeyId { get; }

    X509Certificate2 Certificate { get; }

    /// <summary>The intermediates up to (not including) the root, so a verifier can build the chain without a download.</summary>
    IReadOnlyList<X509Certificate2> Chain { get; }

    byte[] Sign(ReadOnlySpan<byte> data);
}

/// <summary>The IM version 0 signature primitives, shared by the signer and every verifier.</summary>
public static class ImSignature
{
    public static readonly HashAlgorithmName Hash = HashAlgorithmName.SHA256;
    public const DSASignatureFormat Format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation;

    public static string KeyIdOf(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

    /// <summary>True when <paramref name="signature"/> is a valid signature of <paramref name="data"/> by the key of <paramref name="signer"/>. It does not check the chain; ask <see cref="ITrustAnchors"/> for that.</summary>
    public static bool Verify(X509Certificate2 signer, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        using var key = signer.GetECDsaPublicKey();
        if (key is null || key.KeySize != 256 || signature.Length != 64) return false;
        return key.VerifyData(data, signature, Hash, Format);
    }
}

/// <summary><see cref="IImSigner"/> over a certificate that carries its ECDSA private key (in the sandbox: in memory; on a device: a TPM-backed key).</summary>
public sealed class EcdsaImSigner : IImSigner
{
    readonly ECDsa key;

    public EcdsaImSigner(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        key = certificate.GetECDsaPrivateKey() ?? throw new ArgumentException("the certificate carries no ECDSA private key", nameof(certificate));
        if (key.KeySize != 256) throw new ArgumentException("IM version 0 signs with ECDSA P-256 only", nameof(certificate));
        Certificate = certificate;
        Chain = chain;
        KeyId = ImSignature.KeyIdOf(certificate);
    }

    public string KeyId { get; }
    public X509Certificate2 Certificate { get; }
    public IReadOnlyList<X509Certificate2> Chain { get; }

    public byte[] Sign(ReadOnlySpan<byte> data) => key.SignData(data, ImSignature.Hash, ImSignature.Format);
}
