using System.Security.Cryptography;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Tpm;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Tpm;

/// <summary>
/// Doppler's TPM checks on bytes a real TPM 2.0 implementation produced (swtpm with libtpms, tools/tpm-fixtures/make-fixtures.sh), so
/// the parser and the signature checks are not only consistent with Doppler's own software TPM. The fixtures are public: an
/// endorsement key, an attestation key, a device key's public area, a certification and a PCR 7 quote. The credential sealed by
/// <see cref="TpmCredential.Make"/> was opened by that TPM's TPM2_ActivateCredential when the fixtures were made (the script checks it).
/// </summary>
public sealed class TpmFixtureTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    static TpmPublic AttestationKey => TpmPublic.ParseSized(Fixture("ak.pub"));

    [Fact]
    public void The_attestation_key_of_a_real_TPM_is_accepted_and_its_name_is_the_one_the_TPM_reports()
    {
        var checkedKey = TpmEvidence.CheckAttestationKey(AttestationKey.Encoded);

        Assert.NotNull(checkedKey);
        Assert.Equal(Fixture("ak.name"), checkedKey.Name);
    }

    [Fact]
    public void The_endorsement_key_of_a_real_TPM_is_RSA_2048_with_the_default_template_and_imports_as_SubjectPublicKeyInfo()
    {
        var ek = TpmPublic.ParseSized(Fixture("ek.pub"));
        Assert.Equal(TpmAlg.Rsa, ek.Type);
        Assert.Equal(2048, ek.RsaBits);
        Assert.Equal(TpmAlg.Aes, ek.Symmetric);
        Assert.True(ek.Has(TpmObjectAttributes.Restricted | TpmObjectAttributes.Decrypt | TpmObjectAttributes.FixedTpm));

        using var rsa = ek.RsaKey();
        using var imported = TpmEvidence.ImportEndorsementKey(rsa.ExportSubjectPublicKeyInfo());
        Assert.NotNull(imported);
        var sealedCredential = TpmCredential.Make(imported, AttestationKey.Name, RandomNumberGenerator.GetBytes(32));
        Assert.Equal(2 + 2 + 32 + 2 + 32, sealedCredential.CredentialBlob.Length); // TPM2B_ID_OBJECT: integrity HMAC and the encrypted TPM2B_DIGEST
        Assert.Equal(2 + 256, sealedCredential.EncryptedSecret.Length);
    }

    [Fact]
    public void A_real_TPMs_certification_of_a_device_key_verifies_and_yields_its_P256_key()
    {
        var evidence = new KeyAttestation(TpmPublic.ParseSized(Fixture("dev.pub")).Encoded, Fixture("certify.attest"), Fixture("certify.sig"));
        var qualifying = TpmAttest.Parse(evidence.Attest).ExtraData; // tpm2_certify signs with its own qualifying data

        var spki = TpmEvidence.CheckKeyCertification(AttestationKey, evidence, qualifying);

        Assert.NotNull(spki);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(spki, out _);
        Assert.Equal(256, key.KeySize);
        Assert.Null(TpmEvidence.CheckKeyCertification(AttestationKey, evidence, new byte[qualifying.Length]));
    }

    [Fact]
    public void A_real_TPMs_quote_of_PCR_7_verifies_and_its_replay_matches()
    {
        // When the fixtures were made, PCR 7 was extended once with SHA-256("hello"), standing in for one event.
        var quote = new PlatformQuote(Fixture("quote.attest"), Fixture("quote.sig"), TpmAlg.Sha256, Fixture("pcr7.sha256"),
            [new PcrEvent(TcgEventType.EfiAction, SHA256.HashData("hello"u8), "hello"u8.ToArray())]);
        var qualifying = Convert.FromHexString("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");

        Assert.Equal(SecureBootVerdict.NoSeparator, TpmEvidence.CheckQuote(AttestationKey, quote, qualifying)); // one stand-in event, no separator
        Assert.Null(TpmEvidence.CheckQuote(AttestationKey, quote, new byte[32]));
        Assert.Equal(SecureBootVerdict.ReplayMismatch, TpmEvidence.CheckQuote(AttestationKey, quote with { Pcr7Events = [] }, qualifying));
    }

    [Fact]
    public void Every_truncation_of_the_real_structures_is_refused_without_an_exception_escaping()
    {
        foreach (var name in new[] { "certify.attest", "quote.attest" })
        {
            var bytes = Fixture(name);
            for (var n = 0; n < bytes.Length; n++)
                Assert.Throws<TpmFormatException>(() => TpmAttest.Parse(bytes[..n]));
        }
        var ak = AttestationKey.Encoded;
        for (var n = 0; n < ak.Length; n++) Assert.Null(TpmEvidence.CheckAttestationKey(ak[..n]));
    }
}
