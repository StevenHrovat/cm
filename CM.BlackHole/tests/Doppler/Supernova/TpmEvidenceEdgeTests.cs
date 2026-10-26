using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>
/// The edges of Doppler's TPM evidence checks that mutation testing (Supernova level 3, DMP chapter 06) showed no other test pinned:
/// size bounds at and over the limit, a device key on another curve, quotes over another bank or selection, and SecureBoot variable
/// headers whose size fields wrap. Each test changes one thing against an honest baseline that is accepted.
/// </summary>
public sealed class TpmEvidenceEdgeTests : IDisposable
{
    readonly SimulatedTpm tpm = new(DopplerWorld.Good);

    public void Dispose() => tpm.Dispose();

    TpmPublic Aik => TpmPublic.Parse(tpm.AttestationKeyPublic);

    // ---- size bounds ---------------------------------------------------------------------------------------------------

    [Fact]
    public void An_attestation_key_area_of_exactly_1024_bytes_is_read_and_one_byte_more_is_refused()
    {
        var area = Aik;
        // The bytes EncodeRsa writes around the modulus, measured rather than assumed.
        var overhead = TpmPublic.EncodeRsa(area.Attributes, area.Scheme, area.SchemeHash, RandomModulus(256)).Length - 256;
        var atLimit = TpmPublic.EncodeRsa(area.Attributes, area.Scheme, area.SchemeHash, RandomModulus(1024 - overhead));
        var over = TpmPublic.EncodeRsa(area.Attributes, area.Scheme, area.SchemeHash, RandomModulus(1025 - overhead));

        Assert.Equal(1024, atLimit.Length);
        Assert.NotNull(TpmEvidence.CheckAttestationKey(atLimit));
        Assert.Equal(1025, over.Length);
        Assert.Null(TpmEvidence.CheckAttestationKey(over));
    }

    [Fact]
    public void An_endorsement_key_SPKI_of_exactly_2048_bytes_is_read_and_one_byte_more_or_a_1024_bit_key_is_refused()
    {
        var atLimit = SpkiOfLength(2048);
        var over = SpkiOfLength(2049);
        using var small = RSA.Create(1024);

        using (var read = TpmEvidence.ImportEndorsementKey(atLimit)) Assert.NotNull(read);
        Assert.Null(TpmEvidence.ImportEndorsementKey(over));
        Assert.Null(TpmEvidence.ImportEndorsementKey(small.ExportSubjectPublicKeyInfo()));
        Assert.Null(TpmEvidence.ImportEndorsementKey([.. tpm.EndorsementKeySpki, 0])); // trailing bytes
    }

    [Fact]
    public void A_SHA1_named_attestation_key_is_read_only_when_SHA1_is_allowed_and_no_other_name_algorithm_is_ever()
    {
        var area = Aik;
        var sha1 = TpmPublic.EncodeRsa(area.Attributes, area.Scheme, area.SchemeHash, area.RsaModulus, nameAlg: TpmAlg.Sha1);
        var sha384 = TpmPublic.EncodeRsa(area.Attributes, area.Scheme, area.SchemeHash, area.RsaModulus, nameAlg: 0x000C);

        Assert.Null(TpmEvidence.CheckAttestationKey(sha1));
        Assert.NotNull(TpmEvidence.CheckAttestationKey(sha1, allowSha1: true));
        Assert.Null(TpmEvidence.CheckAttestationKey(sha384, allowSha1: true));
        Assert.NotNull(TpmEvidence.CheckAttestationKey(tpm.AttestationKeyPublic, allowSha1: true));
    }

    // ---- the device key ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_certified_device_key_on_another_curve_is_refused()
    {
        tpm.CreateDeviceKey();
        var qualifying = SHA256.HashData([1, 2, 3]);
        var honest = tpm.CertifyDeviceKey(qualifying);
        Assert.NotNull(TpmEvidence.CheckKeyCertification(Aik, honest, qualifying));

        // Same key bytes, curve id P-384 (0x0004) instead of P-256 (0x0003): certified by the same attestation key over the same data.
        var p384 = (byte[])honest.DeviceKeyPublic.Clone();
        var at = IndexOf(p384, [0x00, (byte)TpmAlg.EccNistP256, 0x00, (byte)TpmAlg.Null]);
        p384[at + 1] = 0x04;
        var attest = TpmAttest.EncodeCertify(tpm.AttestationKeyName, qualifying, TpmPublic.Parse(p384).Name);

        Assert.Null(TpmEvidence.CheckKeyCertification(Aik, new KeyAttestation(p384, attest, tpm.SignAsAttestationKey(attest)), qualifying));
    }

    // ---- the quote -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_quote_rebuilt_the_honest_way_is_accepted_so_the_variants_below_differ_in_one_thing()
    {
        var (quote, qualifying) = Quote(TpmAlg.Sha256, 7);
        Assert.Equal(SecureBootVerdict.On, TpmEvidence.CheckQuote(Aik, quote, qualifying));
    }

    [Fact]
    public void A_quote_that_names_another_bank_or_selects_another_bank_or_PCR_is_refused()
    {
        var (honest, qualifying) = Quote(TpmAlg.Sha256, 7);
        Assert.Null(TpmEvidence.CheckQuote(Aik, honest with { Bank = TpmAlg.Sha1 }, qualifying));

        var (sha1Bank, q1) = Quote(TpmAlg.Sha1, 7);
        Assert.Null(TpmEvidence.CheckQuote(Aik, sha1Bank, q1));

        var (pcr0, q0) = Quote(TpmAlg.Sha256, 0);
        Assert.Null(TpmEvidence.CheckQuote(Aik, pcr0, q0));

        // PCR 7 and PCR 8: the second select byte is not zero.
        var (both, q2) = Quote(TpmAlg.Sha256, 7, attest => { attest[^36] = 0x01; });
        Assert.Null(TpmEvidence.CheckQuote(Aik, both, q2));
    }

    [Fact]
    public void A_one_byte_PCR_selection_of_only_PCR_7_is_accepted()
    {
        // sizeofSelect 1 is legal TPM 2.0; the quote then carries [1, 0x80] instead of [3, 0x80, 0, 0].
        var (quote, qualifying) = Quote(TpmAlg.Sha256, 7, rebuild: attest => [.. attest[..^38], 1, 0x80, .. attest[^34..]]);
        Assert.Equal(SecureBootVerdict.On, TpmEvidence.CheckQuote(Aik, quote, qualifying));
    }

    [Fact]
    public void A_PCR_7_value_of_another_length_is_refused_even_when_the_quote_matches_it()
    {
        var qualifying = SHA256.HashData([9]);
        var pcr7 = new byte[20];
        var attest = TpmAttest.EncodeQuote(tpm.AttestationKeyName, qualifying, TpmAlg.Sha256, 7, SHA256.HashData(pcr7));
        var quote = new PlatformQuote(attest, tpm.SignAsAttestationKey(attest), TpmAlg.Sha256, pcr7, []);

        Assert.Null(TpmEvidence.CheckQuote(Aik, quote, qualifying));
    }

    [Fact]
    public void The_checks_that_take_an_attestation_key_need_one()
    {
        Assert.Throws<ArgumentNullException>(() => TpmEvidence.CheckKeyCertification(null!, null, []));
        Assert.Throws<ArgumentNullException>(() => TpmEvidence.CheckQuote(null!, null, []));
        Assert.Throws<ArgumentNullException>(() => SecureBootPolicy.Evaluate(TpmAlg.Sha256, null!, []));
        Assert.Throws<ArgumentNullException>(() => SecureBootPolicy.Evaluate(TpmAlg.Sha256, new byte[32], null!));
    }

    // ---- SecureBoot variable headers -------------------------------------------------------------------------------------

    [Fact]
    public void A_SecureBoot_variable_whose_name_length_wraps_the_size_sum_is_not_read()
    {
        // nameChars = 2^63 + 10: times two it wraps to 20, so 32 + 20 + 1 equals the data length; as an int it is 10, "SecureBoot".
        var data = Variable(nameChars: (1UL << 63) + 10, dataLength: 1, "SecureBoot", [1]);
        Assert.Equal(53, data.Length);

        Assert.Equal(SecureBootVerdict.NotMeasured, Evaluate(data));
    }

    [Fact]
    public void A_SecureBoot_variable_whose_data_length_is_negative_as_a_long_is_not_read_and_does_not_throw()
    {
        // dataLength = 2^64 - 22 is -22 as a long: 32 + 40 - 22 equals the data length of 50, but the 40 name bytes are not there.
        var data = Variable(nameChars: 20, dataLength: ulong.MaxValue - 21, new string('A', 9), []);
        Assert.Equal(50, data.Length);

        Assert.Equal(SecureBootVerdict.NotMeasured, Evaluate(data));
    }

    [Fact]
    public void A_well_formed_SecureBoot_variable_with_value_1_before_the_separator_is_On()
    {
        Assert.Equal(SecureBootVerdict.On, Evaluate(TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1])));
    }

    [Fact]
    public void A_SecureBoot_variable_measured_only_after_the_separator_is_not_measured()
    {
        // Software after the firmware's hand-over can extend PCR 7 too: only a measurement before the separator speaks for the firmware.
        var secureBoot = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]);

        Assert.Equal(SecureBootVerdict.NotMeasured, Replay(Separator(), Event(TcgEventType.EfiVariableDriverConfig, secureBoot)));
        Assert.Equal(SecureBootVerdict.On, Replay(Event(TcgEventType.EfiVariableDriverConfig, secureBoot), Separator(), Event(TcgEventType.EfiVariableDriverConfig, secureBoot)));
    }

    [Fact]
    public void A_separator_sent_under_another_type_still_counts_so_the_hand_over_cannot_move()
    {
        // The type of an event is not measured (GRB-0003 F1; first seen by the step-2 fuzz campaign): a separator is known by its data too, so a
        // relabelled firmware separator still marks the hand-over and a software one after it makes two.
        var secureBoot = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]);
        var relabelled = Event(TcgEventType.EfiAction, new byte[4]);

        Assert.Equal(SecureBootVerdict.NotMeasured, Replay(relabelled, Event(TcgEventType.EfiVariableDriverConfig, secureBoot)));
        Assert.Equal(SecureBootVerdict.NotMeasured, Replay(Event(0xFFFF0004, new byte[4]), Event(TcgEventType.EfiVariableDriverConfig, secureBoot)));
        Assert.Equal(SecureBootVerdict.NoSeparator, Replay(relabelled, Event(TcgEventType.EfiVariableDriverConfig, secureBoot), Separator()));
        Assert.Equal(SecureBootVerdict.ErrorSeparator, Replay(Event(TcgEventType.EfiVariableDriverConfig, secureBoot), Event(TcgEventType.EfiAction, [0xFF, 0xFF, 0xFF, 0xFF])));
        Assert.Equal(SecureBootVerdict.NoSeparator, Replay(Event(TcgEventType.EfiVariableDriverConfig, secureBoot), Separator(), Separator()));
        Assert.Equal(SecureBootVerdict.NoSeparator, Replay(Event(TcgEventType.EfiVariableDriverConfig, secureBoot)));
    }

    [Fact]
    public void A_SecureBoot_variable_is_read_whatever_type_the_agent_gives_it()
    {
        // GRB-0003 F1: the firmware's SecureBoot=0 sent under another type must still say off.
        var on = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]);
        var off = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [0]);

        Assert.Equal(SecureBootVerdict.On, Replay(Event(TcgEventType.EfiVariableAuthority, on), Separator()));
        Assert.Equal(SecureBootVerdict.Off, Replay(Event(TcgEventType.EfiAction, off), Event(TcgEventType.EfiVariableDriverConfig, on), Separator()));
        Assert.Equal(SecureBootVerdict.Off, Replay(Event(TcgEventType.EfiVariableDriverConfig, on), Separator(), Event(0x0000000D, off)));
    }

    [Fact]
    public void Relabelling_the_firmware_events_of_a_Secure_Boot_off_machine_and_extending_two_more_is_refused()
    {
        // GRB-0003 C13/H09: firmware measured SecureBoot=0, PK, KEK, separator, db; software extended SecureBoot=1 and a separator and relabelled
        // the firmware's SecureBoot=0 and separator in the log it sent.
        var on = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]);
        var off = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [0]);
        var pk = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "PK", [7]);

        Assert.NotEqual(SecureBootVerdict.On, Replay(Event(TcgEventType.EfiAction, off), Event(TcgEventType.EfiVariableDriverConfig, pk),
            Event(TcgEventType.EfiAction, new byte[4]), Event(TcgEventType.EfiVariableDriverConfig, on), Separator()));
        // And firmware that measured no SecureBoot variable but a separator (C12): a software SecureBoot=1 plus a second separator is refused.
        Assert.Equal(SecureBootVerdict.NoSeparator, Replay(Event(TcgEventType.EfiVariableDriverConfig, pk), Event(TcgEventType.EfiAction, new byte[4]),
            Event(TcgEventType.EfiVariableDriverConfig, on), Separator()));
    }

    [Fact]
    public void A_firmware_that_measured_nothing_into_PCR7_is_the_documented_limit()
    {
        // GRB-0003 C11/H08: PCR 7 alone cannot tell a sequence software extended into an empty PCR 7 from the firmware's own. This pins the limit
        // stated in docs/doppler.md ("What PCR 7 cannot prove") so a change to it is a decision, not an accident; the enrolment plan is the control.
        var on = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]);

        Assert.Equal(SecureBootVerdict.On, Replay(Event(TcgEventType.EfiVariableDriverConfig, on), Separator()));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------

    (PlatformQuote Quote, byte[] Qualifying) Quote(ushort bank, int pcr, Action<byte[]>? patch = null, Func<byte[], byte[]>? rebuild = null)
    {
        var qualifying = SHA256.HashData(RandomNumberGenerator.GetBytes(16));
        var honest = tpm.QuotePcr7(qualifying);
        var attest = TpmAttest.EncodeQuote(tpm.AttestationKeyName, qualifying, bank, pcr, SHA256.HashData(honest.Pcr7));
        patch?.Invoke(attest);
        if (rebuild is not null) attest = rebuild(attest);
        return (new PlatformQuote(attest, tpm.SignAsAttestationKey(attest), TpmAlg.Sha256, honest.Pcr7, honest.Pcr7Events), qualifying);
    }

    static SecureBootVerdict Evaluate(byte[] variableData)
    {
        PcrEvent[] events =
        [
            new(TcgEventType.EfiVariableDriverConfig, SHA256.HashData(variableData), variableData),
            new(TcgEventType.Separator, SHA256.HashData(new byte[4]), new byte[4]),
        ];
        var pcr = new byte[32];
        foreach (var e in events) pcr = SHA256.HashData([.. pcr, .. e.Digest]);
        return SecureBootPolicy.Evaluate(TpmAlg.Sha256, pcr, events);
    }

    static PcrEvent Event(uint type, byte[] data) => new(type, SHA256.HashData(data), data);

    static PcrEvent Separator() => Event(TcgEventType.Separator, new byte[4]);

    static SecureBootVerdict Replay(params PcrEvent[] events)
    {
        var pcr = new byte[32];
        foreach (var e in events) pcr = SHA256.HashData([.. pcr, .. e.Digest]);
        return SecureBootPolicy.Evaluate(TpmAlg.Sha256, pcr, events);
    }

    static byte[] Variable(ulong nameChars, ulong dataLength, string name, byte[] value)
    {
        var header = new byte[32];
        SecureBootPolicy.EfiGlobalVariable.TryWriteBytes(header.AsSpan(0, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), nameChars);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24), dataLength);
        return [.. header, .. Encoding.Unicode.GetBytes(name), .. value];
    }

    static byte[] RandomModulus(int bytes)
    {
        var modulus = RandomNumberGenerator.GetBytes(bytes);
        modulus[0] |= 0x80;
        modulus[^1] |= 1;
        return modulus;
    }

    /// <summary>A public RSA key's SPKI of exactly <paramref name="length"/> bytes (the modulus is random; only the size matters here).</summary>
    static byte[] SpkiOfLength(int length)
    {
        for (var modulus = length - 40; modulus < length; modulus++)
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = RandomModulus(modulus), Exponent = [1, 0, 1] });
            var spki = rsa.ExportSubjectPublicKeyInfo();
            if (spki.Length == length) return spki;
        }
        throw new InvalidOperationException($"no modulus length gives an SPKI of {length} bytes");
    }

    static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("pattern not found");
    }
}
