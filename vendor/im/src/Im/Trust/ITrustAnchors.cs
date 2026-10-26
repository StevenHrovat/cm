using System.Security.Cryptography.X509Certificates;

namespace Im.Trust;

/// <summary>
/// The trust decision of CMOS: does a certificate chain to the new root and carry the role it claims, at a given time.
/// Milky Way's verifier asks it for the sender (role Device) and for the token signer (role ImTokenSigner); Doppler asks it
/// for Milky Way's gate certificate. The time is always passed in (DMP chapter 10, deterministic tests); nothing here reads a clock.
/// </summary>
public interface ITrustAnchors
{
    ChainVerdict Validate(X509Certificate2 leaf, IReadOnlyList<X509Certificate2> intermediates, TrustRole role, DateTimeOffset at);
}

/// <summary>Serial numbers the issuing CA has revoked. In production it is filled from the issuing CA's CRL, which Milky Way serves; in the sandbox the authority fills it directly.</summary>
public interface IRevocationList
{
    bool IsRevoked(X509Certificate2 certificate);
}
