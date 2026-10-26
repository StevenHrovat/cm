using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CM.BlackHole.Doppler.Tpm;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>
/// TPM2_MakeCredential's building blocks, pinned where mutation testing (Supernova level 3, DMP chapter 06) found them unpinned: KDFa over more than one
/// HMAC block and with a bit count that is not a multiple of 256, and the credential's size bounds.
/// </summary>
public sealed class TpmCredentialEdgeTests
{
    /// <summary>KDFa written out from TPM 2.0 Library Part 1, independently of <see cref="TpmCredential.KdfA"/>.</summary>
    static byte[] Reference(byte[] key, string label, byte[] contextU, byte[] contextV, int bits)
    {
        var output = new List<byte>();
        for (uint counter = 1; output.Count * 8 < bits; counter++)
        {
            var input = new List<byte>();
            var u32 = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(u32, counter);
            input.AddRange(u32);
            input.AddRange(Encoding.ASCII.GetBytes(label));
            input.Add(0);
            input.AddRange(contextU);
            input.AddRange(contextV);
            BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)bits);
            input.AddRange(u32);
            output.AddRange(HMACSHA256.HashData(key, input.ToArray()));
        }
        return [.. output.Take((bits + 7) / 8)];
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(320)]
    [InlineData(512)]
    public void KdfA_matches_the_specification_for_one_block_several_blocks_and_a_partial_last_block(int bits)
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        byte[] name = [0x00, 0x0b, .. Enumerable.Repeat((byte)0xAB, 32)];

        Assert.Equal(Reference(key, "STORAGE", name, [9, 8], bits), TpmCredential.KdfA(key, "STORAGE", name, [9, 8], bits));
    }

    [Fact]
    public void A_credential_is_1_to_32_bytes()
    {
        using var ek = RSA.Create(2048);
        byte[] name = [0x00, 0x0b, .. new byte[32]];

        Assert.Throws<ArgumentException>(() => TpmCredential.Make(ek, name, []));
        Assert.Throws<ArgumentException>(() => TpmCredential.Make(ek, name, new byte[33]));
        Assert.NotNull(TpmCredential.Make(ek, name, new byte[1]));
        Assert.NotNull(TpmCredential.Make(ek, name, new byte[32]));
        Assert.Throws<ArgumentNullException>(() => TpmCredential.Make(null!, name, new byte[32]));
    }

    [Fact]
    public void The_sealed_credential_opens_with_the_endorsement_key_and_the_same_name_only()
    {
        using var ek = RSA.Create(2048);
        byte[] name = [0x00, 0x0b, .. RandomNumberGenerator.GetBytes(32)];
        byte[] other = [0x00, 0x0b, .. RandomNumberGenerator.GetBytes(32)];
        var credential = RandomNumberGenerator.GetBytes(32);

        var blob = TpmCredential.Make(ek, name, credential);

        Assert.Equal(credential, TpmCredential.Activate(ek, name, blob));
        Assert.Null(TpmCredential.Activate(ek, other, blob));
        using var wrongKey = RSA.Create(2048);
        Assert.Null(TpmCredential.Activate(wrongKey, name, blob));
    }
}
