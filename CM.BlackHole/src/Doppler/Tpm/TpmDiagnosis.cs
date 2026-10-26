using System.Security.Cryptography;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>
/// The agent's <c>diagnose</c> command (lane L4b goal 2): runs every step of an enrolment against this machine's TPM and checks each
/// result with Doppler's own checks, without Doppler. It prints the endorsement key id an enrolment plan needs, and for every step "ok"
/// or "FAIL" with the structure's algorithm ids, attributes and sizes, so a format difference on a new platform shows which field it
/// is. It prints only public data (ids, algorithm numbers, attribute names, the PCR 7 value); no key, credential or token. Run it with
/// keys of its own (the Windows agent passes another key prefix), never on the keys of an enrolled machine.
/// </summary>
public static class TpmDiagnosis
{
    /// <summary>Runs the steps and returns true when every check passed.</summary>
    public static bool Run(IDeviceTpm tpm, Action<string> write, bool allowSha1 = true)
    {
        ArgumentNullException.ThrowIfNull(tpm);
        ArgumentNullException.ThrowIfNull(write);
        var ok = true;
        void Step(string name, bool passed, string detail)
        {
            ok &= passed;
            write($"{(passed ? "ok  " : "FAIL")} {name}: {detail}");
        }

        var ekId = Convert.ToHexStringLower(SHA256.HashData(tpm.EndorsementKeySpki));
        using var ek = TpmEvidence.ImportEndorsementKey(tpm.EndorsementKeySpki);
        Step("endorsement key", ek is not null, ek is null ? "not an RSA 2048+ SubjectPublicKeyInfo" : $"RSA {ek.KeySize}, id {ekId} (the id an enrolment plan names)");

        TpmPublic? aik = null;
        try { aik = TpmPublic.Parse(tpm.AttestationKeyPublic); }
        catch (TpmFormatException e) { Step("attestation key", false, "does not parse: " + e.Message); }
        if (aik is not null)
        {
            var accepted = TpmEvidence.CheckAttestationKey(tpm.AttestationKeyPublic, allowSha1) is not null;
            Step("attestation key", accepted, Describe(aik));
        }
        if (ek is null || aik is null) return false;

        var secret = RandomNumberGenerator.GetBytes(32);
        var sealedCredential = TpmCredential.Make(ek, aik.Name, secret);
        var opened = tpm.ActivateCredential(sealedCredential.CredentialBlob, sealedCredential.EncryptedSecret);
        Step("activate credential", opened is not null && CryptographicOperations.FixedTimeEquals(opened, secret),
            opened is null ? "the TPM did not open it" + LastError(tpm) : opened.Length == 32 ? "the TPM opened Doppler's credential" : $"wrong length {opened.Length}");

        byte[] devicePublic;
        try { devicePublic = tpm.CreateDeviceKey(); }
        catch (Exception e) when (e is TpmCommandException or TpmFormatException or CryptographicException or InvalidOperationException)
        {
            Step("device key", false, e.Message);
            return false;
        }
        try { Step("device key", true, Describe(TpmPublic.Parse(devicePublic))); }
        catch (TpmFormatException e) { Step("device key", false, "does not parse: " + e.Message); }

        var csr = tpm.CreateSigningRequest();
        var certifyData = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        try
        {
            var certified = tpm.CertifyDeviceKey(certifyData);
            var spki = TpmEvidence.CheckKeyCertification(aik, certified, certifyData, allowSha1);
            var request = CertificateRequestKey(csr);
            Step("certify", spki is not null && request is not null && spki.AsSpan().SequenceEqual(request),
                $"{DescribeAttest(certified.Attest)}, {DescribeSignature(certified.Signature)}{(spki is null ? ", Doppler's check refused it" : request is null || !spki.AsSpan().SequenceEqual(request) ? ", the request's key is not the certified key" : "")}");
        }
        catch (Exception e) when (e is TpmCommandException or TpmFormatException or CryptographicException)
        {
            Step("certify", false, e.Message);
        }

        var quoteData = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        try
        {
            var quote = tpm.QuotePcr7(quoteData);
            var verdict = TpmEvidence.CheckQuote(aik, quote, quoteData, allowSha1);
            Step("quote", verdict is not null, $"{DescribeAttest(quote.Attest)}, {DescribeSignature(quote.Signature)}, bank 0x{quote.Bank:X4}, PCR 7 {Convert.ToHexStringLower(quote.Pcr7)}, {quote.Pcr7Events.Count} events");
            var replay = SecureBootPolicy.Evaluate(quote.Bank, quote.Pcr7, quote.Pcr7Events);
            Step("secure boot", replay == SecureBootVerdict.On, $"{replay}; events: {string.Join(", ", quote.Pcr7Events.Select(e => $"0x{e.Type:X8}"))}");
        }
        catch (Exception e) when (e is TpmCommandException or TpmFormatException or CryptographicException)
        {
            Step("quote", false, e.Message);
        }

        var reported = tpm.Reported;
        write($"info reported: disk encrypted {reported.DiskEncrypted}, agent package {reported.AgentPackageSha256}");
        return ok;
    }

    static string LastError(IDeviceTpm tpm) => tpm switch
    {
        CommandTpm c when c.LastError is { } rc => $" (TPM 0x{rc:X})",
        _ => "",
    };

    static byte[]? CertificateRequestKey(byte[] csr)
    {
        try
        {
            return System.Security.Cryptography.X509Certificates.CertificateRequest.LoadSigningRequest(csr, HashAlgorithmName.SHA256)
                .PublicKey.ExportSubjectPublicKeyInfo();
        }
        catch (CryptographicException) { return null; }
    }

    static string Describe(TpmPublic key)
    {
        var shape = key.Type == TpmAlg.Rsa ? $"RSA {key.RsaBits}" : key.Type == TpmAlg.Ecc ? $"ECC curve 0x{key.Curve:X4}" : $"type 0x{key.Type:X4}";
        return $"{shape}, nameAlg 0x{key.NameAlg:X4}, scheme 0x{key.Scheme:X4}/0x{key.SchemeHash:X4}, attributes {key.Attributes}";
    }

    static string DescribeAttest(byte[] attest)
    {
        try
        {
            var a = TpmAttest.Parse(attest);
            return $"attest type 0x{a.Type:X4}, {attest.Length} bytes, extraData {a.ExtraData.Length} bytes";
        }
        catch (TpmFormatException e) { return $"attest of {attest.Length} bytes does not parse ({e.Message})"; }
    }

    static string DescribeSignature(byte[] signature)
    {
        try
        {
            var s = TpmSignature.Parse(signature);
            return $"signature 0x{s.Algorithm:X4}/0x{s.Hash:X4}";
        }
        catch (TpmFormatException e) { return $"signature of {signature.Length} bytes does not parse ({e.Message})"; }
    }
}
