using System.Security.Cryptography.X509Certificates;

namespace Im.Trust;

/// <summary>
/// The two masters of the double chain of trust (CM.BlackHole/docs/ca-plan.md section 1a). Each has its own root, issuing CA,
/// register and token signer, and attests a different fact: Doppler that the machine is what it claims and healthy, the operations master that
/// it is supposed to exist in this role.
/// </summary>
public enum TrustMaster
{
    Security,
    Operational,
}

/// <summary>A leaf and its intermediates as one master issued them (the root is never sent).</summary>
public sealed record PresentedChain(X509Certificate2 Leaf, IReadOnlyList<X509Certificate2> Intermediates);

/// <summary>
/// The answer of <see cref="DualTrust.Validate"/>: both chains are always checked, so a refusal is counted per chain (the counters
/// carry a chain dimension), and the key check is separate.
/// </summary>
public readonly record struct DualVerdict(ChainVerdict Security, ChainVerdict Operational, bool SameKey)
{
    /// <summary>Admitted only when both chains are accepted and both leaves certify the same public key.</summary>
    public bool Ok => Security.Ok && Operational.Ok && SameKey;

    /// <summary>The first chain that failed, security first; null when both chains were accepted (the key may still differ).</summary>
    public TrustMaster? FailedChain => !Security.Ok ? TrustMaster.Security : !Operational.Ok ? TrustMaster.Operational : null;
}

/// <summary>
/// The trust decision of the double chain: a component is trusted only when its security chain validates against the security
/// anchors, its operational chain validates against the operational anchors, both for the same role, and both leaves carry the
/// byte-identical SubjectPublicKeyInfo (one device key, two certificates; one proof of possession covers both).
/// Acceptance by the two registers and the two tokens are checked by the verifier on top of this (IM version 1, DMP 04).
/// </summary>
public sealed class DualTrust(ITrustAnchors security, ITrustAnchors operational)
{
    /// <summary>
    /// Builds the decision over two explicit root sets. Throws when a root is in both sets: that configuration would let one master's
    /// chain pass as the other's, and the two chains would no longer be independent.
    /// </summary>
    public static DualTrust Over(
        IReadOnlyList<X509Certificate2> securityRoots, IRevocationList securityRevocations,
        IReadOnlyList<X509Certificate2> operationalRoots, IRevocationList operationalRevocations)
    {
        ArgumentNullException.ThrowIfNull(securityRoots);
        ArgumentNullException.ThrowIfNull(operationalRoots);
        var securityKeys = securityRoots.Select(Spki).ToList();
        foreach (var root in operationalRoots)
        {
            var key = Spki(root);
            if (securityKeys.Any(k => k.AsSpan().SequenceEqual(key)))
                throw new ArgumentException("A root is configured for both masters; the two chains of trust must not share a root or a root key.", nameof(operationalRoots));
        }
        return new DualTrust(new TrustAnchors(securityRoots, securityRevocations), new TrustAnchors(operationalRoots, operationalRevocations));
    }

    public DualVerdict Validate(PresentedChain securityChain, PresentedChain operationalChain, TrustRole role, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(securityChain);
        ArgumentNullException.ThrowIfNull(operationalChain);
        var s = security.Validate(securityChain.Leaf, securityChain.Intermediates, role, at);
        var o = operational.Validate(operationalChain.Leaf, operationalChain.Intermediates, role, at);
        var same = Spki(securityChain.Leaf).AsSpan().SequenceEqual(Spki(operationalChain.Leaf));
        return new DualVerdict(s, o, same);
    }

    static byte[] Spki(X509Certificate2 certificate) => certificate.PublicKey.ExportSubjectPublicKeyInfo();
}
