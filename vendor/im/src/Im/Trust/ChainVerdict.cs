namespace Im.Trust;

/// <summary>Why a certificate was refused. A code, never the certificate's content: refusal logs count codes (CMB rule, counts never content).</summary>
public enum ChainRefusal
{
    None,
    /// <summary>The chain does not end at a root CMOS trusts (the old CA, a self-signed or a foreign certificate).</summary>
    UntrustedRoot,
    Expired,
    NotYetValid,
    /// <summary>The leaf does not carry the EKU of the role asked for, or carries no EKU at all.</summary>
    WrongRole,
    /// <summary>The leaf or an intermediate is on the revocation list of the issuing CA.</summary>
    Revoked,
    /// <summary>Anything else the chain engine reports (bad signature, broken basic constraints, a CA certificate used as a leaf).</summary>
    Malformed,
}

/// <summary>The answer of <see cref="ITrustAnchors.Validate"/>.</summary>
public readonly record struct ChainVerdict(ChainRefusal Refusal)
{
    public bool Ok => Refusal == ChainRefusal.None;

    public static ChainVerdict Accepted { get; } = new(ChainRefusal.None);
}
