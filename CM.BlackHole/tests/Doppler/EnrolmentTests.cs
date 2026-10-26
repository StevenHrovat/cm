using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler;

public sealed class EnrolmentTests : IDisposable
{
    readonly DopplerWorld world = new();

    public void Dispose() => world.Dispose();

    [Fact]
    public void A_planned_machine_with_good_measurements_is_enrolled_and_gets_a_device_certificate_of_the_new_CA()
    {
        var (agent, _, _) = world.NewMachine();
        Assert.Equal(AgentState.Started, agent.Start());
        Assert.True(agent.Enrol());

        Assert.Equal(AgentState.Enrolled, agent.State);
        Assert.StartsWith("dev-", agent.Subject, StringComparison.Ordinal);
        var record = world.Server.Registry.Find(agent.Subject!)!;
        Assert.Equal(EnrolledRole.Laptop, record.Role);
        Assert.False(record.Revoked);
        Assert.Equal(agent.DeviceSigner!.KeyId, record.KeyId);
        Assert.True(world.Authority.Anchors().Validate(agent.DeviceSigner.Certificate, agent.DeviceSigner.Chain, TrustRole.Device, world.Clock.GetUtcNow()).Ok);
        Assert.Equal($"CN={agent.Subject}", agent.DeviceSigner.Certificate.Subject);
    }

    [Fact]
    public void A_machine_without_an_approved_plan_is_refused()
    {
        var (agent, _, _) = world.NewMachine(planned: false);
        agent.Start();
        Assert.False(agent.Enrol());
        Assert.Equal(AgentState.Refused, agent.State);
        Assert.Equal(DopplerRefusal.NoApprovedPlan, agent.LastRefusal);
        Assert.Equal(1, world.Server.Counts["enrol.refused.noapprovedplan"]);
    }

    [Fact]
    public void A_plan_needs_two_distinct_approvers_while_four_eyes_is_on()
    {
        var tpm = world.NewTpm();
        Assert.False(world.Server.AddPlan(new EnrolmentPlan("p1", tpm.EndorsementKeyId, EnrolledRole.Test, ["admin-a"])));
        Assert.False(world.Server.AddPlan(new EnrolmentPlan("p2", tpm.EndorsementKeyId, EnrolledRole.Test, ["admin-a", "ADMIN-A"])));
        Assert.True(world.Server.AddPlan(new EnrolmentPlan("p3", tpm.EndorsementKeyId, EnrolledRole.Test, ["admin-a", "admin-b"])));

        using var oneAdmin = new DopplerWorld(world.Settings with { FourEyes = false });
        Assert.True(oneAdmin.Server.AddPlan(new EnrolmentPlan("p4", tpm.EndorsementKeyId, EnrolledRole.Test, ["admin-a"])));
    }

    [Theory]
    [InlineData(false, true, DopplerWorld.AgentPackage, "enrol.secureboot.off")]
    [InlineData(true, false, DopplerWorld.AgentPackage, "enrol.secureboot.on")]
    [InlineData(true, true, "ffff000000000000000000000000000000000000000000000000000000000000", "enrol.secureboot.on")]
    public void A_machine_that_fails_the_policy_is_refused(bool secureBoot, bool diskEncrypted, string agentPackage, string counted)
    {
        var (agent, _, _) = world.NewMachine(machine: new SimulatedMachine(secureBoot, diskEncrypted, agentPackage));
        agent.Start();
        Assert.False(agent.Enrol());
        Assert.Equal(DopplerRefusal.PolicyFailed, agent.LastRefusal);
        Assert.Equal(1, world.Server.Counts[counted]);
    }

    [Fact]
    public void Both_challenges_are_single_use_and_expire()
    {
        var tpm = Planned();
        var begin = new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic);
        var activation = world.Server.BeginEnrolment(begin).Activation!;
        Assert.Equal(DopplerRefusal.UnknownChallenge, world.Server.BeginEnrolment(begin).Refusal);
        var finish = FinishFor(tpm, activation);
        Assert.True(world.Server.FinishEnrolment(finish).Ok);
        Assert.Equal(DopplerRefusal.UnknownChallenge, world.Server.FinishEnrolment(finish).Refusal);

        var late = Planned();
        var lateActivation = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, late.EndorsementKeySpki, late.AttestationKeyPublic)).Activation!;
        world.Clock.Advance(world.Settings.ChallengeLifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(DopplerRefusal.ExpiredChallenge, world.Server.FinishEnrolment(FinishFor(late, lateActivation)).Refusal);
        Assert.Equal(DopplerRefusal.UnknownChallenge, world.Server.BeginEnrolment(new EnrolBegin(DopplerWorld.RandomBytes(32), late.EndorsementKeySpki, late.AttestationKeyPublic)).Refusal);
    }

    [Fact]
    public void An_attestation_key_from_another_TPM_cannot_open_the_credential_sealed_to_the_planned_one()
    {
        var planned = Planned();
        var impostor = world.NewTpm();

        // The impostor names the planned machine's endorsement key but brings its own attestation key.
        var activation = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, planned.EndorsementKeySpki, impostor.AttestationKeyPublic)).Activation!;
        Assert.Null(impostor.ActivateCredential(activation.CredentialBlob, activation.EncryptedSecret));
        // The planned TPM cannot open it either: it was sealed to the impostor's attestation key name, which the planned TPM does not hold.
        Assert.Null(planned.ActivateCredential(activation.CredentialBlob, activation.EncryptedSecret));

        var guessed = FinishFor(impostor, activation) with { Credential = DopplerWorld.RandomBytes(32) };
        Assert.Equal(DopplerRefusal.BadActivation, world.Server.FinishEnrolment(guessed).Refusal);
    }

    [Theory]
    [InlineData(TpmObjectAttributes.Restricted)]
    [InlineData(TpmObjectAttributes.FixedTpm)]
    [InlineData(TpmObjectAttributes.SensitiveDataOrigin)]
    public void An_attestation_key_that_is_not_restricted_or_not_bound_to_the_TPM_is_refused(TpmObjectAttributes missing)
    {
        var tpm = Planned();
        var area = TpmPublic.Parse(tpm.AttestationKeyPublic);
        var weakened = TpmPublic.EncodeRsa(area.Attributes & ~missing, area.Scheme, area.SchemeHash, area.RsaModulus);

        Assert.Equal(DopplerRefusal.BadAttestationKey, world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, weakened)).Refusal);
    }

    [Fact]
    public void An_attestation_key_that_can_also_decrypt_or_is_too_small_is_refused()
    {
        var tpm = Planned();
        var area = TpmPublic.Parse(tpm.AttestationKeyPublic);
        var decrypting = TpmPublic.EncodeRsa(area.Attributes | TpmObjectAttributes.Decrypt, area.Scheme, area.SchemeHash, area.RsaModulus);
        using var small = System.Security.Cryptography.RSA.Create(1024);
        var tiny = TpmPublic.EncodeRsa(area.Attributes, area.Scheme, area.SchemeHash, small.ExportParameters(false).Modulus!);

        Assert.Equal(DopplerRefusal.BadAttestationKey, world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, decrypting)).Refusal);
        Assert.Equal(DopplerRefusal.BadAttestationKey, world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tiny)).Refusal);
        Assert.Equal(DopplerRefusal.BadAttestationKey, world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, [1, 2, 3])).Refusal);
    }

    [Fact]
    public void A_device_key_certified_by_another_attestation_key_is_refused()
    {
        var tpm = Planned();
        var other = world.NewTpm();
        var activation = Begin(tpm);
        var finish = FinishFor(tpm, activation);
        other.CreateDeviceKey();
        var foreign = other.CertifyDeviceKey(TpmAttest.Parse(finish.Key.Attest).ExtraData);

        Assert.Equal(DopplerRefusal.BadCertify, world.Server.FinishEnrolment(finish with { Key = foreign }).Refusal);
    }

    [Fact]
    public void A_device_key_that_could_leave_the_TPM_is_refused()
    {
        var tpm = Planned();
        var activation = Begin(tpm);
        var finish = FinishFor(tpm, activation);
        var device = TpmPublic.Parse(finish.Key.DeviceKeyPublic);
        var exportable = TpmPublic.EncodeEccP256(device.Attributes & ~TpmObjectAttributes.FixedTpm, device.EccX, device.EccY);
        // Even certified (a TPM would certify any loaded key), the attributes say the key can be duplicated out of the TPM.
        var attest = TpmAttest.EncodeCertify(tpm.AttestationKeyName, TpmAttest.Parse(finish.Key.Attest).ExtraData, TpmPublic.Parse(exportable).Name);
        var key = new KeyAttestation(exportable, attest, tpm.SignAsAttestationKey(attest));

        Assert.Equal(DopplerRefusal.BadCertify, world.Server.FinishEnrolment(finish with { Key = key }).Refusal);
    }

    [Fact]
    public void A_certification_of_another_key_than_the_public_area_sent_is_refused()
    {
        var tpm = Planned();
        var finish = FinishFor(tpm, Begin(tpm));
        var other = world.NewTpm();
        var otherPublic = other.CreateDeviceKey();

        Assert.Equal(DopplerRefusal.BadCertify, world.Server.FinishEnrolment(finish with { Key = finish.Key with { DeviceKeyPublic = otherPublic } }).Refusal);
    }

    [Fact]
    public void Bytes_the_TPM_did_not_generate_are_refused_even_with_a_valid_signature()
    {
        var tpm = Planned();
        var finish = FinishFor(tpm, Begin(tpm));
        var forged = (byte[])finish.Key.Attest.Clone();
        forged[0] ^= 0xFF; // not TPM_GENERATED_VALUE: a restricted key would never sign this

        Assert.Equal(DopplerRefusal.BadCertify, world.Server.FinishEnrolment(finish with { Key = finish.Key with { Attest = forged, Signature = tpm.SignAsAttestationKey(forged) } }).Refusal);
    }

    [Fact]
    public void A_reported_state_changed_after_the_certification_is_refused()
    {
        var tpm = world.NewTpm(DopplerWorld.Good with { DiskEncrypted = false });
        Plan(tpm);
        var finish = FinishFor(tpm, Begin(tpm));

        Assert.Equal(DopplerRefusal.BadCertify, world.Server.FinishEnrolment(finish with { Reported = finish.Reported with { DiskEncrypted = true } }).Refusal);
    }

    [Fact]
    public void A_signing_request_for_another_key_than_the_certified_one_is_refused()
    {
        var tpm = Planned();
        var activation = Begin(tpm);
        var finish = FinishFor(tpm, activation);
        var other = world.NewTpm();
        other.CreateDeviceKey();
        var otherCsr = other.CreateSigningRequest();
        // Re-certify over the other CSR so only the key differs.
        var key = tpm.CertifyDeviceKey(EnrolmentStatements.CertifyData(activation.Challenge, otherCsr, finish.Reported));
        Assert.Equal(DopplerRefusal.KeyMismatch, world.Server.FinishEnrolment(finish with { Key = key, CertificateSigningRequest = otherCsr }).Refusal);

        var second = Planned();
        Assert.Equal(DopplerRefusal.BadRequest, world.Server.FinishEnrolment(FinishFor(second, Begin(second)) with { CertificateSigningRequest = [1, 2, 3] }).Refusal);
    }

    [Fact]
    public void A_quote_over_another_challenge_or_another_PCR_value_is_refused()
    {
        var tpm = Planned();
        var activation = Begin(tpm);
        var finish = FinishFor(tpm, activation);
        var stale = tpm.QuotePcr7(EnrolmentStatements.QuoteData(DopplerWorld.RandomBytes(32)));
        Assert.Equal(DopplerRefusal.BadQuote, world.Server.FinishEnrolment(finish with { Quote = stale }).Refusal);

        var again = Planned();
        var f2 = FinishFor(again, Begin(again));
        var pcr = (byte[])f2.Quote.Pcr7.Clone();
        pcr[0] ^= 1;
        Assert.Equal(DopplerRefusal.BadQuote, world.Server.FinishEnrolment(f2 with { Quote = f2.Quote with { Pcr7 = pcr } }).Refusal);
    }

    [Fact]
    public void A_quote_signed_by_another_attestation_key_is_refused()
    {
        var tpm = Planned();
        var other = world.NewTpm();
        var activation = Begin(tpm);
        var finish = FinishFor(tpm, activation);

        Assert.Equal(DopplerRefusal.BadQuote, world.Server.FinishEnrolment(finish with { Quote = other.QuotePcr7(EnrolmentStatements.QuoteData(activation.Challenge)) }).Refusal);
    }

    [Fact]
    public void An_event_whose_data_was_changed_is_refused_because_its_digest_no_longer_matches()
    {
        var tpm = world.NewTpm(DopplerWorld.Good with { SecureBoot = false });
        Plan(tpm);
        var finish = FinishFor(tpm, Begin(tpm));
        var events = finish.Quote.Pcr7Events.ToList();
        events[0] = events[0] with { Data = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]) };

        Assert.Equal(DopplerRefusal.PolicyFailed, world.Server.FinishEnrolment(finish with { Quote = finish.Quote with { Pcr7Events = events } }).Refusal);
        Assert.Equal(1, world.Server.Counts["enrol.secureboot.eventdigestmismatch"]);
    }

    [Fact]
    public void Secure_Boot_on_extended_by_software_after_boot_does_not_count()
    {
        var tpm = world.NewTpm(DopplerWorld.Good with { SecureBoot = false });
        tpm.ExtendedAfterBoot.Add((TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1])));
        Plan(tpm);

        Assert.Equal(DopplerRefusal.PolicyFailed, world.Server.FinishEnrolment(FinishFor(tpm, Begin(tpm))).Refusal);
        Assert.Equal(1, world.Server.Counts["enrol.secureboot.off"]);
    }

    [Fact]
    public void Leaving_out_events_breaks_the_replay()
    {
        var tpm = Planned();
        var finish = FinishFor(tpm, Begin(tpm));

        Assert.Equal(DopplerRefusal.PolicyFailed, world.Server.FinishEnrolment(finish with { Quote = finish.Quote with { Pcr7Events = finish.Quote.Pcr7Events.Skip(1).ToList() } }).Refusal);
        Assert.Equal(1, world.Server.Counts["enrol.secureboot.replaymismatch"]);
    }

    [Fact]
    public void Re_enrolment_of_the_same_machine_on_a_new_plan_revokes_the_old_certificate()
    {
        var (agent, tpm, _) = world.NewMachine();
        agent.Start();
        agent.Enrol();
        var first = agent.DeviceSigner!;
        var firstSubject = agent.Subject!;
        Assert.True(world.Server.AddPlan(new EnrolmentPlan("p-again", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        Assert.True(agent.Enrol());

        Assert.NotEqual(firstSubject, agent.Subject);
        Assert.True(world.Server.Registry.Find(firstSubject)!.Revoked);
        Assert.Equal(ChainRefusal.Revoked, world.Authority.Anchors().Validate(first.Certificate, first.Chain, TrustRole.Device, world.Clock.GetUtcNow()).Refusal);
        Assert.Equal(1, world.Server.Registry.Count);
    }

    [Fact]
    public void A_plan_is_used_up_by_its_enrolment_so_a_revoked_machine_cannot_enrol_again_on_it()
    {
        var (agent, _, _) = world.NewMachine();
        agent.Start();
        Assert.True(agent.Enrol());
        Assert.True(world.Server.Revoke(agent.Subject!));

        Assert.False(agent.Enrol());
        Assert.Equal(DopplerRefusal.NoApprovedPlan, agent.LastRefusal);
        Assert.Equal(0, world.Server.Registry.Count);
    }

    [Fact]
    public void Challenges_are_bounded_and_the_oldest_open_challenge_makes_room()
    {
        using var small = new DopplerWorld(world.Settings with { MaxOpenChallenges = 2 });
        var first = small.Server.Challenge()!;
        var second = small.Server.Challenge()!;

        // A full table pushes out the oldest challenge instead of refusing everyone (GRB-0003 F2); the table never grows past its cap.
        var third = small.Server.Challenge()!;
        Assert.Equal(1, small.Server.Counts["challenge.evicted"]);
        Assert.Equal(DopplerRefusal.UnknownChallenge, small.Server.IssueToken(new TokenRequest("dev-unknown", first, new byte[64])).Refusal);
        Assert.Equal(DopplerRefusal.NotEnrolled, small.Server.IssueToken(new TokenRequest("dev-unknown", second, new byte[64])).Refusal);
        Assert.Equal(DopplerRefusal.NotEnrolled, small.Server.IssueToken(new TokenRequest("dev-unknown", third, new byte[64])).Refusal);
    }

    [Fact]
    public void A_flood_of_door_challenges_does_not_take_token_renewal_away_from_an_enrolled_machine()
    {
        // GRB-0003 F2: a holder of the shared knock secret asked for far more challenges than the table holds; the enrolled machine still renews.
        using var small = new DopplerWorld(world.Settings with { MaxOpenChallenges = 50 });
        var (agent, tpm, _) = small.NewMachine();
        Assert.True(small.Server.AddPlan(new EnrolmentPlan("p", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        agent.Start();
        Assert.True(agent.Enrol());
        for (var i = 0; i < 500; i++) Assert.NotNull(small.Server.Challenge());

        Assert.True(agent.RefreshToken());
        Assert.False(small.Server.Counts.ContainsKey("challenge.refused.full"));
    }

    [Fact]
    public void An_attestation_key_that_signs_with_SHA_1_as_Windows_makes_it_enrols_while_the_setting_allows_it()
    {
        var tpm = world.NewTpm(attestationHash: TpmAlg.Sha1);
        Plan(tpm);
        Assert.True(Enrol(world, tpm).Ok);

        using var strict = new DopplerWorld(world.Settings with { AllowSha1Attestation = false });
        var refused = strict.NewTpm(attestationHash: TpmAlg.Sha1);
        Assert.True(strict.Server.AddPlan(new EnrolmentPlan("p", refused.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        Assert.Equal(DopplerRefusal.BadCertify, Enrol(strict, refused).Refusal);
    }

    [Fact]
    public void A_signature_with_another_hash_than_the_attestation_key_names_is_refused()
    {
        var tpm = Planned();
        tpm.SignWith = TpmAlg.Sha1;

        Assert.Equal(DopplerRefusal.BadCertify, Enrol(world, tpm).Refusal);
    }

    static EnrolmentResult Enrol(DopplerWorld w, SimulatedTpm tpm)
    {
        var begun = w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic));
        Assert.True(begun.Activation is not null, begun.Refusal.ToString());
        return w.Server.FinishEnrolment(FinishFor(tpm, begun.Activation));
    }

    SimulatedTpm Planned()
    {
        var tpm = world.NewTpm();
        Plan(tpm);
        return tpm;
    }

    void Plan(SimulatedTpm tpm) => Assert.True(world.Server.AddPlan(new EnrolmentPlan("p-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));

    Activation Begin(SimulatedTpm tpm)
    {
        var begun = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic));
        Assert.True(begun.Ok, begun.Refusal.ToString());
        return begun.Activation!;
    }

    /// <summary>What the agent sends in step 2, built honestly from the TPM; tests then change one part.</summary>
    internal static EnrolFinish FinishFor(IDeviceTpm tpm, Activation activation)
    {
        var credential = tpm.ActivateCredential(activation.CredentialBlob, activation.EncryptedSecret) ?? DopplerWorld.RandomBytes(32);
        tpm.CreateDeviceKey();
        var csr = tpm.CreateSigningRequest();
        var key = tpm.CertifyDeviceKey(EnrolmentStatements.CertifyData(activation.Challenge, csr, tpm.Reported));
        var quote = tpm.QuotePcr7(EnrolmentStatements.QuoteData(activation.Challenge));
        return new EnrolFinish(activation.Challenge, credential, key, quote, tpm.Reported, csr);
    }
}
