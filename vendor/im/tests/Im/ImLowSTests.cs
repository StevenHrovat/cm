using System.Numerics;
using Im;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests;

/// <summary>
/// The low-s rule (DMP chapter 04, "Encoding"): ECDSA accepts both <c>(r, s)</c> and <c>(r, n - s)</c> for the same data, so without
/// the rule every signed IM structure had a second valid spelling, against invariant I6. IM signs and accepts the low one only.
/// </summary>
[Trait("Category", "im")]
public sealed class ImLowSTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    static readonly BigInteger Order = new(Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"), isUnsigned: true, isBigEndian: true);

    readonly SandboxAuthority ca = new("sandbox", Now.AddDays(-30));
    readonly IImSigner device;

    public ImLowSTests() => device = ca.Issue(TrustRole.Device, "d-0001", Now.AddDays(-1), Now.AddDays(30));

    public void Dispose() => ca.Dispose();

    static byte[] Flip(byte[] signature)
    {
        var s = new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        var other = (Order - s).ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = signature.ToArray();
        Array.Clear(result, 32, 32);
        other.CopyTo(result, 64 - other.Length);
        return result;
    }

    [Fact]
    public void I6_every_IM_signature_is_low_s_and_its_high_s_twin_is_refused()
    {
        var key = ImKeys.SubjectPublicKeyInfo(device.Certificate);
        for (var i = 0; i < 64; i++)
        {
            var data = BitConverter.GetBytes(i);
            var signature = ImKeys.Sign(device, data);
            Assert.True(ImKeys.IsLowS(signature));
            Assert.True(ImKeys.Verify(key, data, signature));

            var twin = Flip(signature);
            Assert.False(ImKeys.IsLowS(twin));
            Assert.False(ImKeys.Verify(key, data, twin)); // a valid ECDSA signature, but not IM
        }
    }

    [Fact]
    public void Bringing_a_signature_to_low_s_keeps_it_valid_and_is_idempotent()
    {
        var key = ImKeys.SubjectPublicKeyInfo(device.Certificate);
        var seenHigh = false;
        for (var i = 0; i < 64; i++)
        {
            var data = BitConverter.GetBytes(i);
            var raw = device.Sign(data); // the platform's own output: low or high s at random
            seenHigh |= !ImKeys.IsLowS(raw);
            var low = ImKeys.ToLowS(raw);
            Assert.True(ImKeys.Verify(key, data, low));
            Assert.Equal(low, ImKeys.ToLowS(low));
        }
        Assert.True(seenHigh, "64 platform signatures should include a high-s one (probability of none is 2^-64)");
    }
}

/// <summary>Regressions the fuzzer of Supernova level 3 found in the codec (CM.MilkyWay/docs/supernova/README.md, "Findings").</summary>
[Trait("Category", "im")]
public sealed class ImFuzzRegressionTests
{
    [Theory]
    [InlineData(0x80)]
    [InlineData(0xB0)]
    [InlineData(0xFF)]
    public void A_byte_above_0x7F_in_a_printable_id_is_malformed_not_read_as_a_question_mark(byte high)
    {
        var good = new ImHeartbeat(1, ImHeartbeatStatus.Healthy, "0.1.0").Encode();
        var changed = good.ToArray();
        changed[^1] = high; // the last character of the agent version
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImHeartbeat.Parse(changed)).Error);
    }

    [Fact]
    public void A_subject_key_with_explicit_curve_parameters_is_not_canonical_and_never_throws()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var explicitCurve = key.ExportExplicitParameters(false);
        using var withParameters = System.Security.Cryptography.ECDsa.Create(explicitCurve);
        var spki = withParameters.ExportSubjectPublicKeyInfo();
        Assert.False(ImKeys.IsCanonicalP256(spki));
        Assert.False(ImKeys.Verify(spki, [1, 2, 3], new byte[64]));
    }
}
