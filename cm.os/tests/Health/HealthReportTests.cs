using System.Security.Cryptography;
using cm.os.Health;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace cm.os.Tests.Health;

public sealed class HealthReportTests
{
    static readonly DateTimeOffset At = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_signed_bytes_do_not_depend_on_the_order_of_the_counters()
    {
        var a = new HealthReport("x", HealthState.Healthy, At, 1, new Dictionary<string, long> { ["b"] = 2, ["a"] = 1 }, ["t1"]);
        var b = new HealthReport("x", HealthState.Healthy, At, 1, new Dictionary<string, long> { ["a"] = 1, ["b"] = 2 }, ["t1"]);
        Assert.Equal(a.CanonicalBytes(), b.CanonicalBytes());
        Assert.NotEqual(a.CanonicalBytes(), (a with { Sequence = 2 }).CanonicalBytes());
    }

    [Fact]
    public void A_sandbox_issued_leaf_for_a_requested_public_key_verifies_for_its_role_and_carries_no_private_key()
    {
        using var ca = new SandboxAuthority("sandbox", At.AddMonths(-1));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=ignored", key, HashAlgorithmName.SHA256);
        using var leaf = ca.Issue(TrustRole.Device, "dev-1", request.PublicKey, At.AddDays(-1), At.AddDays(1));

        Assert.False(leaf.HasPrivateKey);
        Assert.Equal("CN=dev-1", leaf.Subject);
        Assert.True(ca.Anchors().Validate(leaf, ca.Chain, TrustRole.Device, At).Ok);
    }
}
