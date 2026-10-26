using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests.Trust;

/// <summary>The trust decision of CMOS on the sandbox authority: what is accepted, and every refusal code once.</summary>
public sealed class TrustAnchorsTests : IDisposable
{
    static readonly DateTimeOffset CaFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset LeafFrom = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset LeafTo = new(2027, 6, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    readonly SandboxAuthority ca = new("sandbox", CaFrom);
    readonly SandboxAuthority foreign = new("foreign", CaFrom);

    public void Dispose()
    {
        ca.Dispose();
        foreign.Dispose();
    }

    [Theory]
    [InlineData(TrustRole.Device)]
    [InlineData(TrustRole.Server)]
    [InlineData(TrustRole.ImTokenSigner)]
    [InlineData(TrustRole.MilkyWayGate)]
    [InlineData(TrustRole.DopplerServer)]
    public void A_leaf_of_the_new_authority_is_accepted_for_its_role(TrustRole role)
    {
        var leaf = ca.Issue(role, "leaf", LeafFrom, LeafTo);
        Assert.True(ca.Anchors().Validate(leaf.Certificate, leaf.Chain, role, Now).Ok);
    }

    [Fact]
    public void A_leaf_whose_validity_date_does_not_parse_is_malformed_not_an_exception()
    {
        // GRB-0003 (agent side): the certificate's bytes load, but a bent UTCTime makes the platform's reader throw when the date is read.
        var raw = ca.Issue(TrustRole.Device, "leaf", LeafFrom, LeafTo).Certificate.RawData;
        var at = raw.AsSpan().IndexOf([(byte)0x17, (byte)0x0D]);
        Assert.True(at > 0);
        raw[at + 4] = (byte)'Q';
        X509Certificate2 bent;
        try { bent = X509CertificateLoader.LoadCertificate(raw); }
        catch (CryptographicException) { return; } // a platform that checks dates at load refuses it even earlier
        using (bent) Assert.Equal(ChainRefusal.Malformed, ca.Anchors().Validate(bent, ca.Chain, TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void A_leaf_is_refused_for_a_role_it_does_not_carry()
    {
        var device = ca.Issue(TrustRole.Device, "device", LeafFrom, LeafTo);
        Assert.Equal(ChainRefusal.WrongRole, ca.Anchors().Validate(device.Certificate, device.Chain, TrustRole.ImTokenSigner, Now).Refusal);
    }

    [Fact]
    public void A_leaf_without_any_EKU_is_refused_for_every_role()
    {
        var bare = ca.Issue(TrustRole.Device, "bare", LeafFrom, LeafTo, withRole: false, extraEku: []);
        foreach (var role in Enum.GetValues<TrustRole>())
            Assert.Equal(ChainRefusal.WrongRole, ca.Anchors().Validate(bare.Certificate, bare.Chain, role, Now).Refusal);
    }

    [Fact]
    public void A_leaf_of_another_authority_is_refused_as_untrusted_root()
    {
        // The stand-in for the old CA: a perfectly formed chain that does not end at the new root.
        var old = foreign.Issue(TrustRole.Device, "device", LeafFrom, LeafTo);
        Assert.Equal(ChainRefusal.UntrustedRoot, ca.Anchors().Validate(old.Certificate, old.Chain, TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void A_leaf_presented_with_the_wrong_intermediate_is_refused()
    {
        var leaf = ca.Issue(TrustRole.Device, "device", LeafFrom, LeafTo);
        Assert.Equal(ChainRefusal.UntrustedRoot, ca.Anchors().Validate(leaf.Certificate, [foreign.Issuing], TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void Expired_and_not_yet_valid_leaves_are_refused()
    {
        var leaf = ca.Issue(TrustRole.Device, "device", LeafFrom, LeafTo);
        Assert.Equal(ChainRefusal.Expired, ca.Anchors().Validate(leaf.Certificate, leaf.Chain, TrustRole.Device, LeafTo.AddSeconds(1)).Refusal);
        Assert.Equal(ChainRefusal.NotYetValid, ca.Anchors().Validate(leaf.Certificate, leaf.Chain, TrustRole.Device, LeafFrom.AddSeconds(-1)).Refusal);
    }

    [Fact]
    public void A_revoked_leaf_is_refused()
    {
        var leaf = ca.Issue(TrustRole.Device, "device", LeafFrom, LeafTo);
        ca.Revoke(leaf.Certificate);
        Assert.Equal(ChainRefusal.Revoked, ca.Anchors().Validate(leaf.Certificate, leaf.Chain, TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void A_CA_certificate_used_as_a_leaf_is_refused()
    {
        Assert.Equal(ChainRefusal.Malformed, ca.Anchors().Validate(ca.Issuing, [], TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void A_self_signed_leaf_with_the_right_EKU_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=self", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(TrustOids.Eku(TrustRole.Device))], false));
        using var self = request.CreateSelfSigned(LeafFrom, LeafTo);
        Assert.Equal(ChainRefusal.UntrustedRoot, ca.Anchors().Validate(self, [], TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void A_leaf_on_another_curve_is_refused_as_malformed()
    {
        // Issued by the trusted issuing CA, with the right EKU, but a P-384 key: IM version 0 is P-256 only.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=p384", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(TrustOids.Eku(TrustRole.Device))], false));
        using var anchors = new SandboxAuthorityWithIssuerKey(CaFrom);
        using var leaf = request.Create(anchors.IssuerWithKey, LeafFrom, LeafTo, [1, 2, 3]);
        Assert.Equal(ChainRefusal.Malformed, anchors.Anchors.Validate(leaf, [anchors.Issuing], TrustRole.Device, Now).Refusal);
    }

    [Fact]
    public void A_signature_verifies_with_the_signer_certificate_and_not_with_another()
    {
        var signer = ca.Issue(TrustRole.ImTokenSigner, "doppler", LeafFrom, LeafTo);
        var other = ca.Issue(TrustRole.ImTokenSigner, "other", LeafFrom, LeafTo);
        var data = Encoding.UTF8.GetBytes("IM0/token payload");
        var signature = signer.Sign(data);

        Assert.Equal(64, signature.Length);
        Assert.True(ImSignature.Verify(signer.Certificate, data, signature));
        Assert.False(ImSignature.Verify(other.Certificate, data, signature));
        data[0] ^= 1;
        Assert.False(ImSignature.Verify(signer.Certificate, data, signature));
    }

    [Fact]
    public void The_key_id_is_the_hash_of_the_public_key_and_differs_per_key()
    {
        var a = ca.Issue(TrustRole.Device, "a", LeafFrom, LeafTo);
        var b = ca.Issue(TrustRole.Device, "b", LeafFrom, LeafTo);
        Assert.Equal(64, a.KeyId.Length);
        Assert.Equal(a.KeyId, ImSignature.KeyIdOf(a.Certificate));
        Assert.NotEqual(a.KeyId, b.KeyId);
    }

    [Fact]
    public void Role_OIDs_are_distinct_and_under_the_CM_arc()
    {
        var oids = Enum.GetValues<TrustRole>().Select(TrustOids.Eku).ToList();
        Assert.Equal(oids.Count, oids.Distinct().Count());
        Assert.All(oids, oid => Assert.StartsWith(TrustOids.Arc + ".1.", oid, StringComparison.Ordinal));
    }

    /// <summary>A minimal root and issuing CA whose issuing private key the test can use directly, to forge a leaf the sandbox authority would never issue.</summary>
    sealed class SandboxAuthorityWithIssuerKey : IDisposable
    {
        readonly X509Certificate2 root;

        public SandboxAuthorityWithIssuerKey(DateTimeOffset from)
        {
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var rootRequest = new CertificateRequest("CN=forge root", rootKey, HashAlgorithmName.SHA384);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            root = rootRequest.CreateSelfSigned(from, from.AddYears(10));
            using var issuingKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var issuingRequest = new CertificateRequest("CN=forge issuing", issuingKey, HashAlgorithmName.SHA384);
            issuingRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            issuingRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var issuing = issuingRequest.Create(root, from, from.AddYears(5), [9]);
            IssuerWithKey = issuing.CopyWithPrivateKey(issuingKey);
            Issuing = X509CertificateLoader.LoadCertificate(issuing.RawData);
            Anchors = new TrustAnchors([X509CertificateLoader.LoadCertificate(root.RawData)], new NothingRevoked());
        }

        public X509Certificate2 IssuerWithKey { get; }
        public X509Certificate2 Issuing { get; }
        public ITrustAnchors Anchors { get; }

        public void Dispose()
        {
            root.Dispose();
            IssuerWithKey.Dispose();
            Issuing.Dispose();
        }
    }

    sealed class NothingRevoked : IRevocationList
    {
        public bool IsRevoked(X509Certificate2 certificate) => false;
    }
}
