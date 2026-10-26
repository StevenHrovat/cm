using System.Security.Cryptography;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>
/// Doppler's checks of TPM evidence (lane L4b goal 2). Each returns null (or a non-On verdict) for anything it does not accept, and
/// never throws on hostile bytes. What each check rests on is written next to it; the assumption it cannot check (the endorsement
/// key is a real TPM's) is carried by the enrolment plan, which names the endorsement key and has four eyes.
/// </summary>
public static class TpmEvidence
{
    /// <summary>The bits a TPM-bound key must have: created inside the TPM, never leaves it, never moves to another parent.</summary>
    const TpmObjectAttributes Bound = TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin;

    static bool NameAlgAccepted(ushort alg, bool allowSha1) => alg == TpmAlg.Sha256 || (allowSha1 && alg == TpmAlg.Sha1);

    /// <summary>
    /// The attestation key, when it is an RSA key of at least 2048 bits with a SHA-256 name (SHA-1 when <paramref name="allowSha1"/>) that is restricted to signing what the TPM
    /// itself produced (restricted + sign, not decrypt) and bound to the TPM. A restricted key signs a TPMS_ATTEST only when the TPM
    /// wrote it, so its signature over a certify or quote is the TPM's word. MakeCredential binds to this exact public area.
    /// </summary>
    public static TpmPublic? CheckAttestationKey(byte[]? publicArea, bool allowSha1 = false)
    {
        if (publicArea is null || publicArea.Length > 1024) return null;
        try
        {
            var key = TpmPublic.Parse(publicArea);
            if (key.Type != TpmAlg.Rsa || key.RsaBits < 2048 || key.RsaModulus.Length * 8 != key.RsaBits || !NameAlgAccepted(key.NameAlg, allowSha1)) return null;
            if (!key.Has(Bound | TpmObjectAttributes.Restricted | TpmObjectAttributes.Sign) || key.Has(TpmObjectAttributes.Decrypt)) return null;
            return key;
        }
        catch (TpmFormatException) { return null; }
    }

    /// <summary>The endorsement key from its DER SubjectPublicKeyInfo: RSA, 2048 bits or more (the TCG default EK template).</summary>
    public static RSA? ImportEndorsementKey(byte[]? spki)
    {
        if (spki is null || spki.Length > 2048) return null;
        var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read == spki.Length && rsa.KeySize >= 2048) return rsa;
        }
        catch (CryptographicException) { }
        rsa.Dispose();
        return null;
    }

    /// <summary>
    /// The device key's DER SubjectPublicKeyInfo when the attestation key certified it (TPM2_Certify) over <paramref name="qualifyingData"/>,
    /// and it is an ECC P-256 signing key bound to the TPM (non-exportable) and not restricted.
    /// </summary>
    public static byte[]? CheckKeyCertification(TpmPublic attestationKey, KeyAttestation? evidence, byte[] qualifyingData, bool allowSha1 = false)
    {
        ArgumentNullException.ThrowIfNull(attestationKey);
        if (evidence is null) return null;
        try
        {
            var device = TpmPublic.Parse(evidence.DeviceKeyPublic);
            if (device.Type != TpmAlg.Ecc || device.Curve != TpmAlg.EccNistP256 || !NameAlgAccepted(device.NameAlg, allowSha1)) return null;
            if (!device.Has(Bound | TpmObjectAttributes.Sign) || device.Has(TpmObjectAttributes.Decrypt) || device.Has(TpmObjectAttributes.Restricted)) return null;

            var attest = TpmAttest.Parse(evidence.Attest);
            if (attest.Type != TpmAlg.AttestCertify) return null;
            if (!CryptographicOperations.FixedTimeEquals(attest.ExtraData, qualifyingData)) return null;
            if (!attest.CertifiedName.AsSpan().SequenceEqual(device.Name)) return null;
            if (!TpmSignature.Parse(evidence.Signature).Verifies(attestationKey, evidence.Attest, allowSha1)) return null;
            return device.EccP256SubjectPublicKeyInfo();
        }
        catch (TpmFormatException) { return null; }
    }

    /// <summary>
    /// What PCR 7 says about Secure Boot when the attestation key quoted exactly PCR 7 of the SHA-256 bank over
    /// <paramref name="qualifyingData"/> and the quoted digest is that of the PCR value sent; null when the quote itself is not
    /// accepted. The PCR is then read by <see cref="SecureBootPolicy"/>.
    /// </summary>
    public static SecureBootVerdict? CheckQuote(TpmPublic attestationKey, PlatformQuote? quote, byte[] qualifyingData, bool allowSha1 = false)
    {
        ArgumentNullException.ThrowIfNull(attestationKey);
        if (quote is null || quote.Bank != TpmAlg.Sha256 || quote.Pcr7 is not { Length: 32 } || quote.Pcr7Events is null) return null;
        try
        {
            var attest = TpmAttest.Parse(quote.Attest);
            if (attest.Type != TpmAlg.AttestQuote) return null;
            if (!CryptographicOperations.FixedTimeEquals(attest.ExtraData, qualifyingData)) return null;
            if (attest.PcrSelection.Count != 1 || attest.PcrSelection[0].Hash != TpmAlg.Sha256 || !OnlyPcr7(attest.PcrSelection[0].Select)) return null;
            if (!attest.PcrDigest.AsSpan().SequenceEqual(SHA256.HashData(quote.Pcr7))) return null;
            if (!TpmSignature.Parse(quote.Signature).Verifies(attestationKey, quote.Attest, allowSha1)) return null;
            return SecureBootPolicy.Evaluate(TpmAlg.Sha256, quote.Pcr7, quote.Pcr7Events);
        }
        catch (TpmFormatException) { return null; }
    }

    static bool OnlyPcr7(byte[] select) => select.Length >= 1 && select[0] == 0x80 && select.Skip(1).All(b => b == 0);
}
