using System.Security.Cryptography;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using cm.os.Health;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>
/// The decisions of the Doppler server that mutation testing (Supernova level 3, DMP chapter 06) showed no other test pinned: the exact count and audit
/// trail, the time boundaries (a challenge, a pending enrolment and an issued token at exactly their lifetime), the sweep of a full challenge table,
/// re-enrolment, the plan check inside the enrolling write, the issued-token memory, revocation and the CRL sync, and the admin writes.
/// </summary>
public sealed class DopplerDecisionTests
{
    static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    // ---- the trail ---------------------------------------------------------------------------------------------------

    [Fact]
    public void An_enrolment_a_token_and_a_revocation_leave_exactly_these_counts_and_audit_lines()
    {
        using var w = new DopplerWorld();
        var (agent, tpm, _) = w.NewMachine(planned: false);
        var planId = "plan-" + tpm.EndorsementKeyId[..8];
        Assert.False(w.Server.AddPlan(new EnrolmentPlan(planId, tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a"])));
        Assert.True(w.Server.AddPlan(new EnrolmentPlan(planId, tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        agent.Start();
        Assert.True(agent.Enrol());
        Assert.True(agent.RefreshToken());
        Assert.True(w.Server.Revoke(agent.Subject!));
        Assert.False(agent.RefreshToken());
        Assert.False(agent.RefreshToken());

        Assert.Equal(new Dictionary<string, long>
        {
            ["challenge.issued"] = 4,
            ["enrol.accepted"] = 1,
            ["enrol.begun"] = 1,
            ["enrol.revoked"] = 1,
            ["enrol.secureboot.on"] = 1,
            ["plan.accepted"] = 1,
            ["plan.refused.approvers"] = 1,
            ["token.issued"] = 1,
            ["token.refused.revoked"] = 2,
        }, w.Server.Counts);
        Assert.Equal(
        [
            ("plan.add", planId, "refused: approvers"),
            ("plan.add", planId, "accepted"),
            ("enrol", agent.Subject!, $"accepted: role Laptop, plan {planId}"),
            ("token.issue", agent.Subject!, "accepted"),
            ("revoke", agent.Subject!, "revoked by plan"),
            ("token.issue", "-", "refused: Revoked"),
            ("token.issue", "-", "refused: Revoked"),
        ], w.Audit.Lines.Select(l => (l.Action, l.Target, l.Outcome)));
        Assert.All(w.Audit.Lines, l => Assert.Equal(DopplerWorld.Start, l.At));
    }

    [Fact]
    public void A_missing_or_empty_endorsement_key_is_no_approved_plan_and_not_a_crash()
    {
        using var w = new DopplerWorld();
        var tpm = w.NewTpm();

        Assert.Equal(DopplerRefusal.NoApprovedPlan, w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, null!, tpm.AttestationKeyPublic)).Refusal);
        Assert.Equal(DopplerRefusal.NoApprovedPlan, w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, [], tpm.AttestationKeyPublic)).Refusal);
    }

    [Fact]
    public void A_signing_request_whose_key_names_an_unknown_curve_is_a_bad_request_and_not_a_door_failure()
    {
        // Found by the step-2 fuzz campaign: the platform throws PlatformNotSupportedException for a curve it does not know.
        using var w = new DopplerWorld();
        var tpm = w.NewTpm();
        Assert.True(w.Server.AddPlan(new EnrolmentPlan("p", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        var activation = w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Activation!;
        var finish = EnrolmentTests.FinishFor(tpm, activation);
        byte[] p256 = [0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07];
        var csr = (byte[])finish.CertificateSigningRequest.Clone();
        var at = csr.AsSpan().IndexOf(p256);
        Assert.True(at >= 0, "the honest request names P-256");
        csr[at + p256.Length - 2] = 0x7F; // 1.2.840.10045.3.127.7

        var door = new DopplerDoor(w.Server);
        Assert.Null(door.Answer(DopplerWire.FinishRequest(finish with { CertificateSigningRequest = csr })));
        Assert.Equal(1, w.Server.Counts["enrol.refused.badrequest"]);
        Assert.False(w.Server.Counts.ContainsKey("door.failed"));
    }

    [Fact]
    public void Refusals_at_each_step_name_the_step_in_the_audit_line()
    {
        using var w = new DopplerWorld();
        var tpm = w.NewTpm();
        Assert.Equal(DopplerRefusal.NoApprovedPlan, w.Server.BeginEnrolment(new EnrolBegin(w.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Refusal);
        Assert.Equal(DopplerRefusal.UnknownChallenge, w.Server.FinishEnrolment(Finish(new byte[32])).Refusal);

        Assert.Equal(
        [
            ("enrol.begin", "-", "refused: NoApprovedPlan"),
            ("enrol", "-", "refused: UnknownChallenge"),
        ], w.Audit.Lines.Select(l => (l.Action, l.Target, l.Outcome)));
    }

    [Fact]
    public void Doppler_s_report_carries_the_number_of_enrolled_machines_and_drops_a_revoked_one()
    {
        using var w = new DopplerWorld();
        var a = Enrolled(w);
        Enrolled(w);
        Assert.Equal(2, w.Server.Report().Report.Counts["enrolled"]);

        w.Server.Revoke(a.Subject!);

        Assert.Equal(1, w.Server.Report().Report.Counts["enrolled"]);
    }

    [Fact]
    public void A_Milky_Way_report_that_names_an_unknown_token_raises_the_alarm_once_per_token_and_a_refused_report_is_counted()
    {
        using var w = new DopplerWorld();
        var gate = w.Authority.Issue(TrustRole.MilkyWayGate, "milkyway", DopplerWorld.Start.AddDays(-1), DopplerWorld.Start.AddDays(364));
        using var foreign = new Im.Trust.Sandbox.SandboxAuthority("foreign", DopplerWorld.Start.AddMonths(-1));
        var stranger = foreign.Issue(TrustRole.MilkyWayGate, "milkyway", DopplerWorld.Start.AddDays(-1), DopplerWorld.Start.AddDays(10));

        Assert.Equal(ReportRefusal.None, w.Server.ObserveMilkyWay(MilkyWay(w, gate, 1, ["tok-forged-1", "tok-forged-2"])));
        Assert.Equal(ReportRefusal.Chain, w.Server.ObserveMilkyWay(MilkyWay(w, stranger, 2, [])));

        Assert.Equal(2, w.Server.Counts["alarm.milkyway.unknown_token"]);
        Assert.Equal(1, w.Server.Counts["peer.report.refused.chain"]);
        Assert.Equal(2, w.Audit.Lines.Count(l => l is { Action: "alarm", Target: "doppler", Outcome: "milky way accepted a token doppler never issued" }));
        Assert.Throws<ArgumentNullException>(() => w.Server.ObserveMilkyWay(null!));
    }

    // ---- time boundaries ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_challenge_used_at_exactly_its_lifetime_is_still_good_and_one_tick_later_it_has_expired()
    {
        using var w = new DopplerWorld();
        var onTime = w.Server.Challenge()!;
        var late = w.Server.Challenge()!;
        w.Clock.Advance(w.Settings.ChallengeLifetime);

        Assert.Equal(DopplerRefusal.NotEnrolled, w.Server.IssueToken(new TokenRequest("dev-unknown", onTime, new byte[64])).Refusal);
        w.Clock.Advance(Tick);
        Assert.Equal(DopplerRefusal.ExpiredChallenge, w.Server.IssueToken(new TokenRequest("dev-unknown", late, new byte[64])).Refusal);
    }

    [Fact]
    public void A_pending_enrolment_finished_at_exactly_its_lifetime_is_accepted_and_one_tick_later_it_has_expired()
    {
        using var w = new DopplerWorld();
        var onTime = Planned(w);
        var late = Planned(w);
        var onTimeActivation = Begin(w, onTime);
        var lateActivation = Begin(w, late);
        w.Clock.Advance(w.Settings.ChallengeLifetime);

        Assert.True(w.Server.FinishEnrolment(EnrolmentTests.FinishFor(onTime, onTimeActivation)).Ok);
        w.Clock.Advance(Tick);
        Assert.Equal(DopplerRefusal.ExpiredChallenge, w.Server.FinishEnrolment(EnrolmentTests.FinishFor(late, lateActivation)).Refusal);
    }

    [Fact]
    public void A_full_challenge_table_sweeps_a_challenge_only_after_its_lifetime_and_evicts_the_oldest_until_then()
    {
        using var w = new DopplerWorld(new DopplerSettings { MaxOpenChallenges = 1 });
        var first = w.Server.Challenge()!;
        w.Clock.Advance(w.Settings.ChallengeLifetime);

        // Not expired yet (the lifetime is inclusive): the oldest makes room instead of a refusal (GRB-0003 F2).
        var second = w.Server.Challenge()!;
        Assert.Equal(1, w.Server.Counts["challenge.evicted"]);
        Assert.False(w.Server.Counts.ContainsKey("challenge.refused.full"));
        Assert.Equal(DopplerRefusal.UnknownChallenge, w.Server.IssueToken(new TokenRequest("dev-unknown", first, new byte[64])).Refusal);
        Assert.Equal(DopplerRefusal.NotEnrolled, w.Server.IssueToken(new TokenRequest("dev-unknown", second, new byte[64])).Refusal);

        // An expired challenge is swept, not evicted.
        Assert.NotNull(w.Server.Challenge());
        w.Clock.Advance(w.Settings.ChallengeLifetime + Tick);
        Assert.NotNull(w.Server.Challenge());
        Assert.Equal(1, w.Server.Counts["challenge.evicted"]);
    }

    [Fact]
    public void A_pending_enrolment_holds_its_place_in_the_table_until_its_lifetime_has_passed()
    {
        var settings = new DopplerSettings { MaxOpenChallenges = 1, AllowedAgentPackages = new HashSet<string>([DopplerWorld.AgentPackage], StringComparer.Ordinal) };
        using var w = new DopplerWorld(settings);
        Begin(w, Planned(w));
        w.Clock.Advance(w.Settings.ChallengeLifetime);

        Assert.Null(w.Server.Challenge());
        w.Clock.Advance(Tick);
        Assert.NotNull(w.Server.Challenge());
    }

    [Fact]
    public void A_null_or_short_challenge_is_unknown_without_throwing()
    {
        using var w = new DopplerWorld();

        Assert.Equal(DopplerRefusal.UnknownChallenge, w.Server.IssueToken(new TokenRequest("dev-unknown", null!, new byte[64])).Refusal);
        Assert.Equal(DopplerRefusal.UnknownChallenge, w.Server.IssueToken(new TokenRequest("dev-unknown", new byte[31], new byte[64])).Refusal);
        Assert.Equal(DopplerRefusal.UnknownChallenge, w.Server.FinishEnrolment(Finish(null!)).Refusal);
    }

    // ---- the issued-token memory ---------------------------------------------------------------------------------------

    [Fact]
    public void An_issued_token_id_is_kept_until_its_expiry_plus_the_memory_and_swept_by_the_next_issue_after_that()
    {
        var store = new InMemoryDopplerStore();
        using var w = new DopplerWorld(store: store);
        var agent = Enrolled(w);
        Assert.True(agent.RefreshToken());
        var first = agent.Token!;
        var keepUntil = first.Expires + w.Settings.IssuedTokenMemory;
        Assert.Equal(keepUntil, store.Read().IssuedTokens[first.TokenId]);

        w.Clock.Advance(keepUntil - DopplerWorld.Start);
        Assert.True(agent.RefreshToken());
        Assert.True(store.Read().IssuedTokens.ContainsKey(first.TokenId));

        w.Clock.Advance(Tick);
        Assert.True(agent.RefreshToken());
        Assert.False(store.Read().IssuedTokens.ContainsKey(first.TokenId));
        Assert.Equal(2, store.Read().IssuedTokens.Count);
    }

    // ---- re-enrolment and the plan inside the enrolling write ------------------------------------------------------------

    [Fact]
    public void Re_enrolling_a_machine_revokes_only_its_own_old_entry()
    {
        var store = new InMemoryDopplerStore();
        using var w = new DopplerWorld(store: store);
        var (agent, tpm, _) = w.NewMachine();
        var bystander = Enrolled(w);
        agent.Start();
        Assert.True(agent.Enrol());
        var old = agent.Subject!;

        Assert.True(w.Server.AddPlan(new EnrolmentPlan("plan-again", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        Assert.True(agent.Enrol());

        var register = store.Read().Register;
        Assert.True(register[old].Revoked);
        Assert.False(register[agent.Subject!].Revoked);
        Assert.False(register[bystander.Subject!].Revoked);
        Assert.Contains(w.Audit.Lines, l => l is { Action: "revoke", Outcome: "re-enrolment" } && l.Target == old);
    }

    public static TheoryData<string> Interlopers() => ["removed", "replaced by another plan", "role changed"];

    [Theory]
    [MemberData(nameof(Interlopers))]
    public void A_plan_that_changes_between_the_check_and_the_enrolling_write_is_not_used(string change)
    {
        var store = new RacingStore();
        using var w = new DopplerWorld(store: store);
        var tpm = Planned(w);
        var activation = Begin(w, tpm);
        var ek = tpm.EndorsementKeyId;
        store.Interloper = s => change switch
        {
            "removed" => s with { Plans = s.Plans.Remove(ek) },
            "replaced by another plan" => s with { Plans = s.Plans.SetItem(ek, s.Plans[ek] with { PlanId = "plan-other" }) },
            _ => s with { Plans = s.Plans.SetItem(ek, s.Plans[ek] with { Role = EnrolledRole.Build }) },
        };

        var result = w.Server.FinishEnrolment(EnrolmentTests.FinishFor(tpm, activation));

        Assert.Equal(DopplerRefusal.NoApprovedPlan, result.Refusal);
        Assert.Empty(store.Read().Register);
        Assert.False(w.Server.Counts.ContainsKey("enrol.accepted"));
    }

    // ---- revocation and the CRL ----------------------------------------------------------------------------------------

    [Fact]
    public void Revoking_an_unknown_machine_or_one_already_revoked_is_false_and_leaves_no_trail()
    {
        using var w = new DopplerWorld();
        var agent = Enrolled(w);
        Assert.True(w.Server.Revoke(agent.Subject!));
        var lines = w.Audit.Lines.Count;

        Assert.False(w.Server.Revoke(agent.Subject!));
        Assert.False(w.Server.Revoke("dev-unknown"));

        Assert.Equal(1, w.Server.Counts["enrol.revoked"]);
        Assert.Equal(lines, w.Audit.Lines.Count);
    }

    [Fact]
    public void An_admin_revocation_reaches_the_CRL_on_the_next_sync_once_and_an_enrolled_machine_never_does()
    {
        var store = new InMemoryDopplerStore();
        using var w = new DopplerWorld(store: store);
        var revoked = Enrolled(w);
        var kept = Enrolled(w);

        Assert.True(DopplerAdmin.Revoke(store, revoked.Subject!, w.Clock.GetUtcNow()));
        Assert.True(store.Read().Register[revoked.Subject!].Revoked);
        Assert.False(store.Read().Register[revoked.Subject!].OnCrl);

        Assert.Equal(1, w.Server.SyncRevocations());
        Assert.Equal(0, w.Server.SyncRevocations());

        Assert.True(store.Read().Register[revoked.Subject!].OnCrl);
        Assert.False(store.Read().Register[kept.Subject!].OnCrl);
        Assert.True(w.Authority.IsRevoked(Certificate(store, revoked.Subject!)));
        Assert.False(w.Authority.IsRevoked(Certificate(store, kept.Subject!)));
    }

    // ---- the admin writes ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("g")]          // one character that is not hex
    [InlineData("")]           // 63 characters
    public void A_plan_whose_endorsement_key_id_is_not_64_hex_characters_is_refused(string tail)
    {
        using var w = new DopplerWorld();
        var id = new string('a', 63) + tail;

        Assert.False(DopplerAdmin.AddPlan(new InMemoryDopplerStore(), w.Settings, new EnrolmentPlan("p", id, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
    }

    [Fact]
    public void Removing_a_plan_says_whether_there_was_one()
    {
        var store = new InMemoryDopplerStore();
        var settings = new DopplerSettings();
        var id = new string('b', 64);
        Assert.True(DopplerAdmin.AddPlan(store, settings, new EnrolmentPlan("p", id, EnrolledRole.Test, ["admin-a", "admin-b"])));

        Assert.True(DopplerAdmin.RemovePlan(store, id));
        Assert.False(DopplerAdmin.RemovePlan(store, id));
        Assert.Empty(store.Read().Plans);
    }

    [Fact]
    public void The_admin_writes_and_the_server_entry_points_need_their_arguments()
    {
        using var w = new DopplerWorld();
        var store = new InMemoryDopplerStore();
        var plan = new EnrolmentPlan("p", new string('c', 64), EnrolledRole.Test, ["admin-a", "admin-b"]);

        Assert.Throws<ArgumentNullException>(() => DopplerAdmin.AddPlan(null!, w.Settings, plan));
        Assert.Throws<ArgumentNullException>(() => DopplerAdmin.AddPlan(store, null!, plan));
        Assert.Throws<ArgumentNullException>(() => DopplerAdmin.AddPlan(store, w.Settings, null!));
        Assert.Throws<ArgumentNullException>(() => DopplerAdmin.Revoke(null!, "dev-x", DopplerWorld.Start));
        Assert.Throws<ArgumentNullException>(() => DopplerAdmin.RemovePlan(null!, plan.EndorsementKeyId));
        Assert.Throws<ArgumentNullException>(() => w.Server.BeginEnrolment(null!));
        Assert.Throws<ArgumentNullException>(() => w.Server.FinishEnrolment(null!));
        Assert.Throws<ArgumentNullException>(() => w.Server.IssueToken(null!));
        Assert.Throws<ArgumentNullException>(() => EnrolmentStatements.CertifyData(new byte[32], [1], null!));
    }

    // ---- the signed statements -------------------------------------------------------------------------------------------

    [Fact]
    public void The_statements_are_these_bytes_so_agent_and_server_of_different_builds_agree()
    {
        // Computed independently of Canonical: SHA-256 over the tag and each part, every one prefixed with its length as a big-endian int32.
        var challenge = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        Assert.Equal("ec142373fb1de3c715b799a38535bb3cf2e01bab80d7123750b168af23fe5a94",
            Convert.ToHexStringLower(EnrolmentStatements.CertifyData(challenge, [1, 2, 3], new ReportedState(true, DopplerWorld.AgentPackage))));
        Assert.Equal("50863b1845dca917d7a07f18a4afbe7f1a6b35526ce3565f3fb2060b7c725584",
            Convert.ToHexStringLower(EnrolmentStatements.QuoteData(challenge)));
        Assert.Equal("362a13bf590a97730f17dfcf5fa671e3565aa519b3abaa751f1f1a8241fa32b9",
            Convert.ToHexStringLower(SHA256.HashData(EnrolmentStatements.TokenProof("dev-0011223344556677", challenge))));
        Assert.NotEqual(EnrolmentStatements.CertifyData(challenge, [1, 2, 3], new ReportedState(false, DopplerWorld.AgentPackage)),
            EnrolmentStatements.CertifyData(challenge, [1, 2, 3], new ReportedState(true, DopplerWorld.AgentPackage)));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>An in-memory store whose next update first applies <see cref="Interloper"/>: another writer that got in between Doppler's read and its write.</summary>
    sealed class RacingStore : IDopplerStore
    {
        readonly InMemoryDopplerStore inner = new();

        public Func<DopplerState, DopplerState>? Interloper { get; set; }

        public DopplerState Read() => inner.Read();

        public DopplerState Update(Func<DopplerState, DopplerState> change)
        {
            if (Interloper is { } other)
            {
                Interloper = null;
                inner.Update(other);
            }
            return inner.Update(change);
        }
    }

    /// <summary>A step 2 request that never gets past the challenge check.</summary>
    static EnrolFinish Finish(byte[] challenge) =>
        new(challenge, new byte[32], new KeyAttestation([], [], []), new PlatformQuote([], [], 0, [], []), new ReportedState(true, DopplerWorld.AgentPackage), []);

    static SignedHealthReport MilkyWay(DopplerWorld w, IImSigner signer, long sequence, IReadOnlyList<string> accepted) =>
        SignedHealthReport.Sign(new HealthReport("milkyway", HealthState.Healthy, w.Clock.GetUtcNow(), sequence,
            new Dictionary<string, long> { ["im.accepted"] = accepted.Count }, accepted), signer);

    static System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate(IDopplerStore store, string subject) =>
        System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(store.Read().Register[subject].Certificate);

    static SimulatedTpm Planned(DopplerWorld w)
    {
        var tpm = w.NewTpm();
        Assert.True(w.Server.AddPlan(new EnrolmentPlan("p-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        return tpm;
    }

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
