using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Im.Trust;

namespace Im;

/// <summary>
/// The domain tags of IM version 0 (DMP chapter 04, "Encoding"). Every signature and MAC covers its tag, then one zero byte, then
/// the canonical bytes, so a signature made for one purpose never verifies as another.
/// </summary>
public static class ImTags
{
    public const string Knock = "IM0/knock";
    public const string Accept = "IM0/accept";
    public const string Response = "IM0/response";
    public const string Token = "IM0/token";
    public const string Message = "IM0/message";

    /// <summary>The bytes a signature or MAC covers: the tag, a zero byte, then the parts in order.</summary>
    public static byte[] Input(string tag, params ReadOnlySpan<byte[]> parts)
    {
        var writer = new ImWriter().Raw(Encoding.ASCII.GetBytes(tag)).U8(0);
        foreach (var part in parts) writer.Raw(part);
        return writer.ToArray();
    }
}

/// <summary>
/// Keys and signatures of IM version 0: ECDSA P-256 with SHA-256, signatures in the 64-byte IEEE P1363 form. A subject key travels
/// as a SubjectPublicKeyInfo inside the token; this class is the one place that turns those bytes back into a key.
/// </summary>
public static class ImKeys
{
    const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>SHA-256 of a SubjectPublicKeyInfo, the 32 bytes behind a KeyId.</summary>
    public static byte[] KeyIdBytes(ReadOnlySpan<byte> subjectPublicKeyInfo) => SHA256.HashData(subjectPublicKeyInfo);

    /// <summary>The lower-case hex KeyId, the same value as <see cref="ImSignature.KeyIdOf"/> for the certificate that carries the key.</summary>
    public static string KeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) => Convert.ToHexStringLower(KeyIdBytes(subjectPublicKeyInfo));

    public static byte[] SubjectPublicKeyInfo(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.PublicKey.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// True when the bytes are exactly the canonical SubjectPublicKeyInfo of an ECDSA key on the named curve P-256: it imports, the
    /// curve is P-256 by name (no explicit parameters, no other 256-bit curve), and exporting it again gives the same bytes.
    /// </summary>
    public static bool IsCanonicalP256(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var read);
            if (read != subjectPublicKeyInfo.Length || key.KeySize != 256) return false;
            var curve = key.ExportParameters(false).Curve;
            if (!curve.IsNamed || !(curve.Oid.Value == P256Oid || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256")) return false;
            return key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(subjectPublicKeyInfo);
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException or NotSupportedException or ArgumentException)
        {
            // A SubjectPublicKeyInfo with explicit or unknown curve parameters makes the platform throw PlatformNotSupportedException,
            // not CryptographicException (found by the fuzzer); any of them means "not canonical P-256".
            return false;
        }
    }

    /// <summary>
    /// Verifies a 64-byte P1363 signature with a key given as SubjectPublicKeyInfo. False for any key that is not canonical P-256 and for
    /// a signature whose <c>s</c> is in the upper half of the group order: ECDSA accepts both <c>(r, s)</c> and <c>(r, n - s)</c>, and
    /// IM allows only the low one, so every signed structure has exactly one valid spelling (invariant I6).
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> subjectPublicKeyInfo, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != ImLimits.SignatureLength || !IsLowS(signature) || !IsCanonicalP256(subjectPublicKeyInfo)) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return key.VerifyData(data, signature, ImSignature.Hash, ImSignature.Format);
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException or NotSupportedException or ArgumentException)
        {
            // A SubjectPublicKeyInfo with explicit or unknown curve parameters makes the platform throw PlatformNotSupportedException,
            // not CryptographicException (found by the fuzzer); any of them means "not canonical P-256".
            return false;
        }
    }

    /// <summary>
    /// Signs with a signer from <c>Im.Trust</c>, checks the result has the one shape IM version 0 allows, and brings <c>s</c> into the
    /// lower half of the group order (the low-s rule of <see cref="Verify"/>).
    /// </summary>
    public static byte[] Sign(IImSigner signer, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(signer);
        var signature = signer.Sign(data);
        if (signature.Length != ImLimits.SignatureLength) throw new InvalidOperationException("the signer did not return a 64-byte P1363 signature");
        return ToLowS(signature);
    }

    /// <summary>The order n of the P-256 group, big-endian.</summary>
    static readonly BigInteger Order = new(Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"), isUnsigned: true, isBigEndian: true);

    static readonly BigInteger HalfOrder = Order / 2;

    /// <summary>True when the <c>s</c> half of a 64-byte P1363 signature is at most n / 2.</summary>
    public static bool IsLowS(ReadOnlySpan<byte> signature) =>
        signature.Length == ImLimits.SignatureLength && new BigInteger(signature[32..], isUnsigned: true, isBigEndian: true) <= HalfOrder;

    /// <summary>The same signature with <c>s</c> replaced by <c>n - s</c> when <c>s</c> is in the upper half. Both verify under ECDSA; only the result is IM.</summary>
    public static byte[] ToLowS(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != ImLimits.SignatureLength) throw new ArgumentException("not a 64-byte P1363 signature", nameof(signature));
        if (IsLowS(signature)) return signature;
        var s = new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        var low = (Order - s).ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = signature.ToArray();
        Array.Clear(result, 32, 32);
        low.CopyTo(result, 64 - low.Length);
        return result;
    }
}

/// <summary>Certificate chains as IM carries them: the end-entity certificate first, then its intermediates, never the root.</summary>
public static class ImChains
{
    /// <summary>The chain of a signer in wire order (its certificate, then <see cref="IImSigner.Chain"/>), as DER.</summary>
    public static IReadOnlyList<byte[]> Of(IImSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        var chain = new List<byte[]> { signer.Certificate.RawData };
        chain.AddRange(signer.Chain.Select(c => c.RawData));
        return chain;
    }

    /// <summary>
    /// Loads a wire chain into certificates. Throws <see cref="ImFormatException"/> when a certificate does not parse; the caller
    /// disposes the result.
    /// </summary>
    public static LoadedChain Load(IReadOnlyList<byte[]> der)
    {
        ArgumentNullException.ThrowIfNull(der);
        var certificates = new List<X509Certificate2>(der.Count);
        try
        {
            foreach (var item in der) certificates.Add(X509CertificateLoader.LoadCertificate(item));
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException or NotSupportedException or ArgumentException)
        {
            foreach (var c in certificates) c.Dispose();
            throw new ImFormatException(ImFormatError.Malformed);
        }
        return new LoadedChain(certificates);
    }
}

/// <summary>A chain loaded from the wire: <see cref="EndEntity"/> and its <see cref="Intermediates"/>.</summary>
public sealed class LoadedChain(IReadOnlyList<X509Certificate2> certificates) : IDisposable
{
    public X509Certificate2 EndEntity => certificates[0];

    public IReadOnlyList<X509Certificate2> Intermediates { get; } = certificates.Skip(1).ToArray();

    public void Dispose()
    {
        foreach (var c in certificates) c.Dispose();
    }
}
