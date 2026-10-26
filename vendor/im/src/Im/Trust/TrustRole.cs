namespace Im.Trust;

/// <summary>
/// The roles a certificate from the new certificate authority can carry (CM.BlackHole/docs/ca-plan.md, section "Profiles").
/// A role is an extended key usage (EKU) on the leaf certificate. The verifier never accepts a certificate for a role whose
/// EKU it does not carry, and a leaf without any EKU is refused (an empty EKU would mean "any purpose" to a plain chain check).
/// </summary>
public enum TrustRole
{
    /// <summary>An enrolled machine (roles: runner, dev-vm, test, build, laptop). Identifies the sender of an IM message.</summary>
    Device,
    /// <summary>A CMOS server that offers TLS (the operations master, Sagittarius nodes, the Milky Way host's internal side).</summary>
    Server,
    /// <summary>Doppler's authority key that signs IM tokens. Only Doppler holds one.</summary>
    ImTokenSigner,
    /// <summary>The Milky Way gate itself (both ports).</summary>
    MilkyWayGate,
    /// <summary>The Doppler server (its own identity, separate from the token-signing key).</summary>
    DopplerServer,
}

/// <summary>
/// The object identifiers of the roles. The arc is a UUID-based OID (ITU-T X.667, arc 2.25): it needs no registration and
/// names the CM framework, not the company, so it may live in an open repository. Changing a value is a contract change
/// (constitution C4): every certificate issued under the old value stops verifying.
/// </summary>
public static class TrustOids
{
    /// <summary>2.25 followed by the decimal value of UUID 20e0922f-a222-4b6d-a027-b3296fc64985 (generated once for CM).</summary>
    public const string Arc = "2.25.43701335367132911904664927645766928773";

    /// <summary>Server authentication (RFC 5280); a Server or MilkyWayGate certificate carries it next to its role OID.</summary>
    public const string ServerAuth = "1.3.6.1.5.5.7.3.1";

    /// <summary>Client authentication (RFC 5280); a Device certificate carries it next to its role OID for mutual TLS.</summary>
    public const string ClientAuth = "1.3.6.1.5.5.7.3.2";

    /// <summary>The EKU OID of a role: the arc, then 1 (EKU branch), then the role number.</summary>
    public static string Eku(TrustRole role) => role switch
    {
        TrustRole.Device => Arc + ".1.1",
        TrustRole.Server => Arc + ".1.2",
        TrustRole.ImTokenSigner => Arc + ".1.3",
        TrustRole.MilkyWayGate => Arc + ".1.4",
        TrustRole.DopplerServer => Arc + ".1.5",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unknown trust role"),
    };
}
