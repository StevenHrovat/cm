using System.Security.Cryptography.X509Certificates;

namespace Im.Trust;

/// <summary>
/// The issuing CA as Doppler sees it (CM.BlackHole/docs/ca-plan.md section 4: one constrained endpoint, Doppler the only caller).
/// Doppler has already checked the request against a profile; the issuer sets every extension from the role itself and never copies
/// an extension a requester asked for. The sandbox implementation is <see cref="Sandbox.SandboxAuthority"/>.
/// </summary>
public interface ICertificateIssuer
{
    /// <summary>The intermediates every issued leaf chains through (the issuing CA certificate).</summary>
    IReadOnlyList<X509Certificate2> Chain { get; }

    /// <summary>Issues a leaf for <paramref name="key"/> (the public key of a verified request) with the profile of <paramref name="role"/>. The result carries no private key.</summary>
    X509Certificate2 Issue(TrustRole role, string subject, PublicKey key, DateTimeOffset notBefore, DateTimeOffset notAfter);

    /// <summary>Puts a certificate on the issuing CA's revocation list.</summary>
    void Revoke(X509Certificate2 certificate);
}
