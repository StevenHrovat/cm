using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>
/// TPM2_MakeCredential done in software by Doppler (TCG TPM 2.0 Library Part 1, 24 "Credential Protection"), and the matching
/// TPM2_ActivateCredential for the software TPM of the sandbox. A credential sealed to a TPM's endorsement key and an attestation
/// key's name can only be recovered by that TPM, and only while that key with exactly that public area is loaded in it. Doppler
/// sends a fresh random secret this way and the agent proves it got it back: so the attestation key provably lives in the TPM whose
/// endorsement key the enrolment plan names.
/// The endorsement key is the TCG default RSA 2048 template (EK credential profile, template L-1): nameAlg SHA-256, AES-128 CFB.
/// </summary>
public static class TpmCredential
{
    const string IdentityLabel = "IDENTITY";

    /// <summary>The two TPM2B blobs ActivateCredential takes: the credential blob (TPM2B_ID_OBJECT) and the seed (TPM2B_ENCRYPTED_SECRET).</summary>
    public sealed record Sealed(byte[] CredentialBlob, byte[] EncryptedSecret);

    public static Sealed Make(RSA endorsementKey, byte[] attestationKeyName, byte[] credential)
    {
        ArgumentNullException.ThrowIfNull(endorsementKey);
        if (credential.Length is 0 or > 32) throw new ArgumentException("a credential is 1 to 32 bytes", nameof(credential));
        var seed = RandomNumberGenerator.GetBytes(32);
        var encryptedSeed = OaepEncrypt(endorsementKey, seed, IdentityLabel);
        var symKey = KdfA(seed, "STORAGE", attestationKeyName, [], 128);
        var plain = new TpmWriter().Sized(credential).ToArray();
        var encIdentity = Cfb(symKey, plain, encrypt: true);
        var hmacKey = KdfA(seed, "INTEGRITY", [], [], 256);
        var integrity = HMACSHA256.HashData(hmacKey, (byte[])[.. encIdentity, .. attestationKeyName]);
        var idObject = new TpmWriter().Sized(integrity).Raw(encIdentity).ToArray();
        return new Sealed(new TpmWriter().Sized(idObject).ToArray(), new TpmWriter().Sized(encryptedSeed).ToArray());
    }

    /// <summary>
    /// The software TPM's ActivateCredential: recovers the credential with the endorsement key's private part, refusing a blob that was
    /// made for another key name. Never used with a real TPM (that does it inside).
    /// </summary>
    public static byte[]? Activate(RSA endorsementKey, byte[] attestationKeyName, Sealed blob)
    {
        ArgumentNullException.ThrowIfNull(endorsementKey);
        ArgumentNullException.ThrowIfNull(blob);
        try
        {
            var seedReader = new TpmReader(blob.EncryptedSecret);
            var seed = OaepDecrypt(endorsementKey, seedReader.Sized(512), IdentityLabel);
            seedReader.End();
            if (seed is null) return null;

            var outer = new TpmReader(blob.CredentialBlob);
            var idObject = outer.Sized(1024);
            outer.End();
            var r = new TpmReader(idObject);
            var integrity = r.Sized(64);
            var encIdentity = idObject[r.Position..];
            var hmacKey = KdfA(seed, "INTEGRITY", [], [], 256);
            if (!CryptographicOperations.FixedTimeEquals(integrity, HMACSHA256.HashData(hmacKey, (byte[])[.. encIdentity, .. attestationKeyName]))) return null;

            var symKey = KdfA(seed, "STORAGE", attestationKeyName, [], 128);
            var plain = Cfb(symKey, encIdentity, encrypt: false);
            var p = new TpmReader(plain);
            var credential = p.Sized(32);
            p.End();
            return credential;
        }
        catch (TpmFormatException)
        {
            return null;
        }
    }

    /// <summary>KDFa with SHA-256 (TPM 2.0 Library Part 1, section "KDFa"): counter mode HMAC over counter, label, a zero byte, contextU, contextV and the bit count.</summary>
    public static byte[] KdfA(byte[] key, string label, byte[] contextU, byte[] contextV, int bits)
    {
        var bytes = (bits + 7) / 8;
        var output = new byte[bytes];
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var written = 0;
        for (uint counter = 1; written < bytes; counter++)
        {
            var input = new TpmWriter().U32(counter).Raw(labelBytes).U8(0).Raw(contextU).Raw(contextV).U32((uint)bits).ToArray();
            var block = HMACSHA256.HashData(key, input);
            var take = Math.Min(block.Length, bytes - written);
            block.AsSpan(0, take).CopyTo(output.AsSpan(written));
            written += take;
        }
        return output;
    }

    /// <summary>AES-CFB with full-block feedback and a zero IV, as the TPM does it: a stream mode, the last block is cut, no padding (which .NET's CFB does not allow).</summary>
    static byte[] Cfb(byte[] key, byte[] input, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var output = new byte[input.Length];
        var feedback = new byte[16];
        var stream = new byte[16];
        for (var offset = 0; offset < input.Length; offset += 16)
        {
            aes.EncryptEcb(feedback, stream, PaddingMode.None);
            var n = Math.Min(16, input.Length - offset);
            for (var i = 0; i < n; i++) output[offset + i] = (byte)(input[offset + i] ^ stream[i]);
            var cipher = encrypt ? output : input;
            if (n == 16) Array.Copy(cipher, offset, feedback, 0, 16);
        }
        return output;
    }

    // ---- RSA-OAEP with a label (RFC 8017 7.1; .NET's OAEP has no label, the TPM's needs one) ---------------------------------

    static byte[] LabelHash(string label) => SHA256.HashData([.. Encoding.ASCII.GetBytes(label), 0]);

    internal static byte[] OaepEncrypt(RSA key, byte[] message, string label)
    {
        var p = key.ExportParameters(false);
        var k = p.Modulus!.Length;
        const int hLen = 32;
        if (message.Length > k - 2 * hLen - 2) throw new ArgumentException("message too long for OAEP", nameof(message));
        var db = new byte[k - hLen - 1];
        LabelHash(label).CopyTo(db, 0);
        db[db.Length - message.Length - 1] = 1;
        message.CopyTo(db, db.Length - message.Length);
        var seed = RandomNumberGenerator.GetBytes(hLen);
        Xor(db, Mgf1(seed, db.Length));
        Xor(seed, Mgf1(db, hLen));
        var em = new byte[k];
        seed.CopyTo(em, 1);
        db.CopyTo(em, 1 + hLen);
        var c = BigInteger.ModPow(new BigInteger(em, isUnsigned: true, isBigEndian: true), new BigInteger(p.Exponent, isUnsigned: true, isBigEndian: true), new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true));
        return LeftPad(c.ToByteArray(isUnsigned: true, isBigEndian: true), k);
    }

    internal static byte[]? OaepDecrypt(RSA key, byte[] cipher, string label)
    {
        var p = key.ExportParameters(true);
        var k = p.Modulus!.Length;
        const int hLen = 32;
        if (cipher.Length != k) return null;
        var m = BigInteger.ModPow(new BigInteger(cipher, isUnsigned: true, isBigEndian: true), new BigInteger(p.D, isUnsigned: true, isBigEndian: true), new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true));
        var em = LeftPad(m.ToByteArray(isUnsigned: true, isBigEndian: true), k);
        if (em[0] != 0) return null;
        var seed = em[1..(1 + hLen)];
        var db = em[(1 + hLen)..];
        Xor(seed, Mgf1(db, hLen));
        Xor(db, Mgf1(seed, db.Length));
        if (!CryptographicOperations.FixedTimeEquals(db.AsSpan(0, hLen), LabelHash(label))) return null;
        var i = hLen;
        while (i < db.Length && db[i] == 0) i++;
        if (i == db.Length || db[i] != 1) return null;
        return db[(i + 1)..];
    }

    static byte[] Mgf1(byte[] seed, int length)
    {
        var output = new byte[length];
        var written = 0;
        for (uint counter = 0; written < length; counter++)
        {
            var block = SHA256.HashData([.. seed, .. new TpmWriter().U32(counter).ToArray()]);
            var take = Math.Min(block.Length, length - written);
            block.AsSpan(0, take).CopyTo(output.AsSpan(written));
            written += take;
        }
        return output;
    }

    static void Xor(byte[] target, byte[] mask)
    {
        for (var i = 0; i < target.Length; i++) target[i] ^= mask[i];
    }

    static byte[] LeftPad(byte[] value, int length)
    {
        if (value.Length >= length) return value;
        var padded = new byte[length];
        value.CopyTo(padded, length - value.Length);
        return padded;
    }
}
