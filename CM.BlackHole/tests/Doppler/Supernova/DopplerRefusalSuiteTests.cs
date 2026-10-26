using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>
/// The refusal suite of Supernova level 3 for Doppler (DMP chapter 06): every refusal reason Doppler knows has a scenario that produces
/// it, the answer names that reason, exactly one refusal is counted, under that reason's key, and no audit line or count carries
/// content. A reason added to <see cref="DopplerRefusal"/> without a scenario here fails <see cref="Every_refusal_reason_has_a_scenario"/>,
/// so the suite cannot fall behind the code. Each scenario runs on a fresh world.
/// </summary>
public sealed class DopplerRefusalSuiteTests
{
    /// <summary>Reasons Doppler never answers itself, with the written reason (the mutation and refusal rules allow none without one).</summary>
    static readonly Dictionary<DopplerRefusal, string> NotDopplers = new()
    {
        [DopplerRefusal.None] = "not a refusal",
        [DopplerRefusal.Unanswered] = "the agent's own reason when the door gives no answer; Doppler's counts say why (AgentLoopPairTests)",
        [DopplerRefusal.BadAnswer] = "the agent's own reason when Doppler's enrolment answer does not hold up (AgentAnswerTests)",
    };

    /// <summary>A scenario returns the refusal Doppler answered and the counter key it should have counted under.</summary>
    static readonly Dictionary<DopplerRefusal, Func<DopplerWorld, (DopplerRefusal Answer, string Counted)>> Scenarios = new()
    {
        [DopplerRefusal.UnknownChallenge] = w =>
        {
            var tpm = Planned(w);
            return (w.Server.BeginEnrolment(new EnrolBegin(DopplerWorld.RandomBytes(32), tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Refusal, "enrol.refused.unknownchallenge");
        },
        [DopplerRefusal.ExpiredChallenge] = w =>
        {
            var tpm = Planned(w);
            var activation = Begin(w, tpm);
            w.Clock.Advance(w.Settings.ChallengeLifetime + TimeSpan.FromSeconds(1));
            return (w.Server.FinishEnrolment(EnrolmentTests.FinishFor(tpm, activation)).Refusal, "enrol.refused.expiredchallenge");
        },
        [DopplerRefusal.NoApprovedPlan] = w =>
        {
            var tpm = w.NewTpm();
            return (w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Refusal, "enrol.refused.noapprovedplan");
        },
        [DopplerRefusal.BadAttestationKey] = w =>
        {
            var tpm = Planned(w);
            var area = TpmPublic.Parse(tpm.AttestationKeyPublic);
            var unrestricted = TpmPublic.EncodeRsa(area.Attributes & ~TpmObjectAttributes.Restricted, area.Scheme, area.SchemeHash, area.RsaModulus);
            return (w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, tpm.EndorsementKeySpki, unrestricted)).Refusal, "enrol.refused.badattestationkey");
        },
        [DopplerRefusal.BadActivation] = w =>
        {
            var tpm = Planned(w);
            var finish = EnrolmentTests.FinishFor(tpm, Begin(w, tpm)) with { Credential = DopplerWorld.RandomBytes(32) };
            return (w.Server.FinishEnrolment(finish).Refusal, "enrol.refused.badactivation");
        },
        [DopplerRefusal.BadCertify] = w =>
        {
            var tpm = Planned(w);
            var finish = EnrolmentTests.FinishFor(tpm, Begin(w, tpm));
            var other = w.NewTpm();
            other.CreateDeviceKey();
            var foreign = other.CertifyDeviceKey(TpmAttest.Parse(finish.Key.Attest).ExtraData);
            return (w.Server.FinishEnrolment(finish with { Key = foreign }).Refusal, "enrol.refused.badcertify");
        },
        [DopplerRefusal.BadQuote] = w =>
        {
            var tpm = Planned(w);
            var finish = EnrolmentTests.FinishFor(tpm, Begin(w, tpm));
            return (w.Server.FinishEnrolment(finish with { Quote = tpm.QuotePcr7(EnrolmentStatements.QuoteData(DopplerWorld.RandomBytes(32))) }).Refusal, "enrol.refused.badquote");
        },
        [DopplerRefusal.KeyMismatch] = w =>
        {
            var tpm = Planned(w);
            var activation = Begin(w, tpm);
            var finish = EnrolmentTests.FinishFor(tpm, activation);
            var other = w.NewTpm();
            other.CreateDeviceKey();
            var otherCsr = other.CreateSigningRequest();
            var key = tpm.CertifyDeviceKey(EnrolmentStatements.CertifyData(activation.Challenge, otherCsr, finish.Reported));
            return (w.Server.FinishEnrolment(finish with { Key = key, CertificateSigningRequest = otherCsr }).Refusal, "enrol.refused.keymismatch");
        },
        [DopplerRefusal.PolicyFailed] = w =>
        {
            var tpm = w.NewTpm(DopplerWorld.Good with { SecureBoot = false });
            Plan(w, tpm);
            return (w.Server.FinishEnrolment(EnrolmentTests.FinishFor(tpm, Begin(w, tpm))).Refusal, "enrol.refused.policyfailed");
        },
        [DopplerRefusal.BadRequest] = w =>
        {
            var tpm = Planned(w);
            var finish = EnrolmentTests.FinishFor(tpm, Begin(w, tpm)) with { CertificateSigningRequest = [1, 2, 3] };
            return (w.Server.FinishEnrolment(finish).Refusal, "enrol.refused.badrequest");
        },
        [DopplerRefusal.NotEnrolled] = w =>
            (w.Server.IssueToken(new TokenRequest("dev-unknown", w.Server.Challenge()!, new byte[64])).Refusal, "token.refused.notenrolled"),
        [DopplerRefusal.Revoked] = w =>
        {
            var agent = Enrolled(w);
            w.Server.Revoke(agent.Subject!);
            var challenge = w.Server.Challenge()!;
            var proof = agent.DeviceSigner!.Sign(EnrolmentStatements.TokenProof(agent.Subject!, challenge));
            return (w.Server.IssueToken(new TokenRequest(agent.Subject!, challenge, proof)).Refusal, "token.refused.revoked");
        },
        [DopplerRefusal.BadProof] = w =>
        {
            var agent = Enrolled(w);
            var other = w.NewTpm();
            other.CreateDeviceKey();
            var challenge = w.Server.Challenge()!;
            var forged = other.SignWithDeviceKey(EnrolmentStatements.TokenProof(agent.Subject!, challenge));
            return (w.Server.IssueToken(new TokenRequest(agent.Subject!, challenge, forged)).Refusal, "token.refused.badproof");
        },
    };

    public static TheoryData<DopplerRefusal> Reasons() => [.. Scenarios.Keys];

    [Fact]
    public void Every_refusal_reason_has_a_scenario()
    {
        var missing = Enum.GetValues<DopplerRefusal>().Where(r => !Scenarios.ContainsKey(r) && !NotDopplers.ContainsKey(r)).ToList();
        Assert.True(missing.Count == 0, "no scenario for: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Each_reason_is_answered_and_counted_once_under_its_own_key(DopplerRefusal reason)
    {
        using var world = new DopplerWorld();
        var before = Refusals(world);

        var (answer, counted) = Scenarios[reason](world);

        Assert.Equal(reason, answer);
        var after = Refusals(world);
        var grown = after.Where(p => p.Value != before.GetValueOrDefault(p.Key)).Select(p => (p.Key, p.Value - before.GetValueOrDefault(p.Key))).ToList();
        Assert.Equal([(counted, 1L)], grown);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public void No_count_key_or_audit_line_of_a_refusal_carries_content(DopplerRefusal reason)
    {
        using var world = new DopplerWorld();
        Scenarios[reason](world);

        // Count keys are fixed words; audit targets are plan ids and subjects, outcomes are words. No hex longer than an id, no base64.
        Assert.All(world.Server.Counts.Keys, k => Assert.Matches("^[a-z0-9._]+$", k));
        Assert.All(world.Audit.Lines, l =>
        {
            Assert.Matches("^[a-z0-9._]+$", l.Action);
            Assert.DoesNotMatch("[0-9a-fA-F]{40,}|[A-Za-z0-9+/]{40,}={0,2}", l.Target + " " + l.Outcome);
        });
    }

    static Dictionary<string, long> Refusals(DopplerWorld w) =>
        w.Server.Counts.Where(p => p.Key.Contains(".refused.", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value);

    static SimulatedTpm Planned(DopplerWorld w)
    {
        var tpm = w.NewTpm();
        Plan(w, tpm);
        return tpm;
    }

    static void Plan(DopplerWorld w, SimulatedTpm tpm) =>
        Assert.True(w.Server.AddPlan(new EnrolmentPlan("p-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));

    static Activation Begin(DopplerWorld w, SimulatedTpm tpm)
    {
        var begun = w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic));
        Assert.True(begun.Ok, begun.Refusal.ToString());
        return begun.Activation!;
    }

    static DopplerAgent Enrolled(DopplerWorld w)
    {
        var (agent, _, _) = w.NewMachine();
        agent.Start();
        Assert.True(agent.Enrol());
        return agent;
    }
}
