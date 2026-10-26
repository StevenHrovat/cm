using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Im.Trust.Sandbox;

/// <summary>
/// An in-memory two-tier certificate authority (a root and one issuing CA) that stands in for the new CA in tests and in the
/// simulator. It mirrors the profiles of CM.BlackHole/docs/ca-plan.md (roles as EKUs, ECDSA P-256 leaves, the issuing CA
/// with path length 0) so a test that passes here exercises the same checks the real chain will meet.
/// Never used outside the sandbox: a real deployment's certificates come from the issuing CA, and its roots are configured.
/// A second instance is a convenient "foreign" authority (an old-CA stand-in) whose certificates must be refused.
/// </summary>
public sealed class SandboxAuthority : IRevocationList, ICertificateIssuer, IDisposable
{
    readonly X509Certificate2 root;
    readonly X509Certificate2 issuing;
    readonly HashSet<string> revoked = new(StringComparer.Ordinal);
    readonly Lock gate = new();

    /// <param name="name">A label for the subjects (a role word such as "sandbox", never a company value).</param>
    /// <param name="validFrom">Start of the CA validity; the CAs are valid for ten years from it. Passed in, never read from a clock.</param>
    public SandboxAuthority(string name, DateTimeOffset validFrom)
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var rootRequest = new CertificateRequest($"CN={name} root", rootKey, HashAlgorithmName.SHA384);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        root = rootRequest.CreateSelfSigned(validFrom, validFrom.AddYears(10));

        using var issuingKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var issuingRequest = new CertificateRequest($"CN={name} issuing", issuingKey, HashAlgorithmName.SHA384);
        issuingRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        issuingRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        issuingRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(issuingRequest.PublicKey, false));
        issuingRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, true, false));
        using var issuedPublic = issuingRequest.Create(root, validFrom, validFrom.AddYears(5), NewSerial());
        issuing = issuedPublic.CopyWithPrivateKey(issuingKey);
    }

    /// <summary>The root to configure as the only trust anchor (public part only).</summary>
    public X509Certificate2 Root => X509CertificateLoader.LoadCertificate(root.RawData);

    /// <summary>The issuing CA (public part only), the one intermediate of every chain.</summary>
    public X509Certificate2 Issuing => X509CertificateLoader.LoadCertificate(issuing.RawData);

    /// <summary>Trust anchors that trust this authority's root only and honour its revocations.</summary>
    public ITrustAnchors Anchors() => new TrustAnchors([Root], this);

    /// <summary>Issues a leaf for a role with a fresh ECDSA P-256 key. <paramref name="extraEku"/> lets a test build a deliberately wrong certificate (for example no role EKU at all with <c>[]</c> and <paramref name="withRole"/> false).</summary>
    public IImSigner Issue(TrustRole role, string subject, DateTimeOffset notBefore, DateTimeOffset notAfter, bool withRole = true, IEnumerable<string>? extraEku = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subject}", key, HashAlgorithmName.SHA256);
        using var issuedPublic = Sign(request, role, notBefore, notAfter, withRole, extraEku);
        return new EcdsaImSigner(issuedPublic.CopyWithPrivateKey(key), [Issuing]);
    }

    /// <inheritdoc />
    public IReadOnlyList<X509Certificate2> Chain => [Issuing];

    /// <inheritdoc />
    public X509Certificate2 Issue(TrustRole role, string subject, PublicKey key, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        ArgumentNullException.ThrowIfNull(key);
        var request = new CertificateRequest(new X500DistinguishedName($"CN={subject}"), key, HashAlgorithmName.SHA256);
        return Sign(request, role, notBefore, notAfter, withRole: true, extraEku: null);
    }

    X509Certificate2 Sign(CertificateRequest request, TrustRole role, DateTimeOffset notBefore, DateTimeOffset notAfter, bool withRole, IEnumerable<string>? extraEku)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var ekus = new OidCollection();
        if (withRole) ekus.Add(new Oid(TrustOids.Eku(role)));
        foreach (var oid in extraEku ?? DefaultExtraEku(role)) ekus.Add(new Oid(oid));
        if (ekus.Count > 0) request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(ekus, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuing, true, false));
        return request.Create(issuing, notBefore, notAfter, NewSerial());
    }

    /// <summary>Puts a certificate on the issuing CA's revocation list (the sandbox CRL).</summary>
    public void Revoke(X509Certificate2 certificate)
    {
        lock (gate) revoked.Add(certificate.SerialNumber);
    }

    public bool IsRevoked(X509Certificate2 certificate)
    {
        lock (gate) return revoked.Contains(certificate.SerialNumber);
    }

    public void Dispose()
    {
        root.Dispose();
        issuing.Dispose();
    }

    static IEnumerable<string> DefaultExtraEku(TrustRole role) => role switch
    {
        TrustRole.Device => [TrustOids.ClientAuth],
        TrustRole.Server or TrustRole.MilkyWayGate or TrustRole.DopplerServer => [TrustOids.ServerAuth],
        _ => [],
    };

    static byte[] NewSerial()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F; // positive INTEGER
        return serial;
    }
}
