using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests.Trust;

/// <summary>The double chain of trust (ca-plan.md section 1a): one device key, a leaf from each master, both required.</summary>
public sealed class DualTrustTests : IDisposable
{
    static readonly DateTimeOffset CaFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset LeafFrom = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset LeafTo = new(2027, 6, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    readonly SandboxAuthority doppler = new("security", CaFrom);
    readonly SandboxAuthority sirius = new("operational", CaFrom);
    readonly SandboxAuthority foreign = new("foreign", CaFrom);
    readonly ECDsa deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        doppler.Dispose();
        sirius.Dispose();
        foreign.Dispose();
        deviceKey.Dispose();
    }

    DualTrust Trust() => DualTrust.Over([doppler.Root], doppler, [sirius.Root], sirius);

    PresentedChain Leaf(SandboxAuthority authority, ECDsa key, TrustRole role = TrustRole.Device) =>
        new(authority.Issue(role, "dev-0001", new PublicKey(key), LeafFrom, LeafTo), authority.Chain);

    [Fact]
    public void A_device_key_certified_by_both_masters_is_trusted()
    {
        var verdict = Trust().Validate(Leaf(doppler, deviceKey), Leaf(sirius, deviceKey), TrustRole.Device, Now);
        Assert.True(verdict.Ok);
        Assert.Null(verdict.FailedChain);
    }

    [Fact]
    public void A_security_chain_from_another_authority_is_refused_and_named()
    {
        var verdict = Trust().Validate(Leaf(foreign, deviceKey), Leaf(sirius, deviceKey), TrustRole.Device, Now);
        Assert.False(verdict.Ok);
        Assert.Equal(TrustMaster.Security, verdict.FailedChain);
        Assert.Equal(ChainRefusal.UntrustedRoot, verdict.Security.Refusal);
        Assert.True(verdict.Operational.Ok);
    }

    [Fact]
    public void A_revoked_operational_leaf_is_refused_though_the_security_chain_holds()
    {
        var operationalLeaf = Leaf(sirius, deviceKey);
        sirius.Revoke(operationalLeaf.Leaf);
        var verdict = Trust().Validate(Leaf(doppler, deviceKey), operationalLeaf, TrustRole.Device, Now);
        Assert.False(verdict.Ok);
        Assert.Equal(TrustMaster.Operational, verdict.FailedChain);
        Assert.Equal(ChainRefusal.Revoked, verdict.Operational.Refusal);
    }

    [Fact]
    public void One_master_chain_presented_twice_does_not_pass_as_both()
    {
        var security = Leaf(doppler, deviceKey);
        var verdict = Trust().Validate(security, security, TrustRole.Device, Now);
        Assert.Equal(TrustMaster.Operational, verdict.FailedChain);
        Assert.Equal(ChainRefusal.UntrustedRoot, verdict.Operational.Refusal);
    }

    [Fact]
    public void Two_valid_leaves_for_different_keys_are_refused()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verdict = Trust().Validate(Leaf(doppler, deviceKey), Leaf(sirius, otherKey), TrustRole.Device, Now);
        Assert.Null(verdict.FailedChain);
        Assert.False(verdict.SameKey);
        Assert.False(verdict.Ok);
    }

    [Fact]
    public void A_role_one_master_did_not_grant_is_refused()
    {
        var verdict = Trust().Validate(Leaf(doppler, deviceKey, TrustRole.Server), Leaf(sirius, deviceKey, TrustRole.Device), TrustRole.Server, Now);
        Assert.Equal(TrustMaster.Operational, verdict.FailedChain);
        Assert.Equal(ChainRefusal.WrongRole, verdict.Operational.Refusal);
    }

    [Fact]
    public void Both_chains_are_checked_even_when_the_first_fails()
    {
        var verdict = Trust().Validate(Leaf(foreign, deviceKey), Leaf(foreign, deviceKey), TrustRole.Device, Now);
        Assert.Equal(ChainRefusal.UntrustedRoot, verdict.Security.Refusal);
        Assert.Equal(ChainRefusal.UntrustedRoot, verdict.Operational.Refusal);
    }

    [Fact]
    public void A_root_configured_for_both_masters_is_refused_at_setup()
    {
        Assert.Throws<ArgumentException>(() => DualTrust.Over([doppler.Root], doppler, [sirius.Root, doppler.Root], sirius));
    }
}
