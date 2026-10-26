using System.Security.Cryptography.X509Certificates;

namespace Im.Trust;

/// <summary>
/// <see cref="ITrustAnchors"/> over an explicit set of roots (custom root trust: the machine's certificate store is never
/// consulted, so the old CA, which domain machines trust, is not trusted here). Revocation comes from an
/// <see cref="IRevocationList"/> instead of fetching CRLs, so a check never waits on the network.
/// </summary>
public sealed class TrustAnchors(IReadOnlyList<X509Certificate2> roots, IRevocationList revocations) : ITrustAnchors
{
    public ChainVerdict Validate(X509Certificate2 leaf, IReadOnlyList<X509Certificate2> intermediates, TrustRole role, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(intermediates);
        // A certificate whose bytes parse but whose fields do not (a bent validity date, found by GRB-0003 on the agent's side) throws from the
        // platform's reader; that is a malformed certificate, never an exception out of a trust decision.
        try { return Decide(leaf, intermediates, role, at); }
        catch (System.Security.Cryptography.CryptographicException) { return new(ChainRefusal.Malformed); }
    }

    ChainVerdict Decide(X509Certificate2 leaf, IReadOnlyList<X509Certificate2> intermediates, TrustRole role, DateTimeOffset at)
    {
        if (at < leaf.NotBefore.ToUniversalTime()) return new(ChainRefusal.NotYetValid);
        if (at > leaf.NotAfter.ToUniversalTime()) return new(ChainRefusal.Expired);
        if (IsCa(leaf) || !IsP256(leaf)) return new(ChainRefusal.Malformed);
        if (!CarriesRole(leaf, role)) return new(ChainRefusal.WrongRole);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        foreach (var root in roots) chain.ChainPolicy.CustomTrustStore.Add(root);
        foreach (var intermediate in intermediates) chain.ChainPolicy.ExtraStore.Add(intermediate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = at.UtcDateTime;
        chain.ChainPolicy.VerificationTimeIgnored = false;
        chain.ChainPolicy.DisableCertificateDownloads = true;

        var built = chain.Build(leaf);
        var flags = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, s) => all | s.Status);
        if (!built || flags != X509ChainStatusFlags.NoError)
        {
            if ((flags & (X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain)) != 0) return new(ChainRefusal.UntrustedRoot);
            if ((flags & X509ChainStatusFlags.NotTimeValid) != 0) return new(ChainRefusal.Expired);
            if ((flags & X509ChainStatusFlags.NotValidForUsage) != 0) return new(ChainRefusal.WrongRole);
            return new(ChainRefusal.Malformed);
        }

        foreach (var element in chain.ChainElements)
            if (revocations.IsRevoked(element.Certificate)) return new(ChainRefusal.Revoked);
        return ChainVerdict.Accepted;
    }

    /// <summary>The leaf must carry the role's EKU explicitly; a certificate without an EKU extension is refused for every role.</summary>
    static bool CarriesRole(X509Certificate2 leaf, TrustRole role)
    {
        var wanted = TrustOids.Eku(role);
        foreach (var extension in leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>())
            foreach (var oid in extension.EnhancedKeyUsages)
                if (oid.Value == wanted) return true;
        return false;
    }

    /// <summary>IM version 0 accepts ECDSA P-256 leaves only (the one algorithm of <see cref="ImSignature"/>).</summary>
    static bool IsP256(X509Certificate2 leaf)
    {
        using var key = leaf.GetECDsaPublicKey();
        if (key is null) return false;
        var curve = key.ExportParameters(false).Curve;
        return curve.IsNamed && (curve.Oid.Value == "1.2.840.10045.3.1.7" || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256");
    }

    static bool IsCa(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(b => b.CertificateAuthority);
}
