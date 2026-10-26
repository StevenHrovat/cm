using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>
/// The findings F2 to F6 of the first GRB-0003 run (2026-10-25), each as the behaviour the fix makes true. F1 lives with the other PCR 7 cases in
/// <see cref="TpmEvidenceEdgeTests"/>; F2's flood is in <c>EnrolmentTests</c>. The run's own tests stay with its report, not in this repository.
/// </summary>
public sealed class Grb0003RegressionTests : IDisposable
{
    const TpmObjectAttributes Bound = TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin | TpmObjectAttributes.UserWithAuth;

    readonly DopplerWorld world = new();

    public void Dispose() => world.Dispose();

    // ---- F4: malformed inputs are refused with a code, never thrown -------------------------------------------------------------

    [Fact]
    public void A_device_public_area_whose_point_is_not_on_the_curve_is_a_bad_certification()
    {
        var tpm = Planned(world);
        var activation = Begin(world, tpm);
        var finish = EnrolmentTests.FinishFor(tpm, activation);
        var offCurve = TpmPublic.EncodeEccP256(Bound | TpmObjectAttributes.Sign, Bytes(32, 1), Bytes(32, 2));
        var certifyData = EnrolmentStatements.CertifyData(activation.Challenge, finish.CertificateSigningRequest, finish.Reported);
        var attest = TpmAttest.EncodeCertify(tpm.AttestationKeyName, certifyData, TpmPublic.Parse(offCurve).Name);

        var result = world.Server.FinishEnrolment(finish with { Key = new KeyAttestation(offCurve, attest, tpm.SignAsAttestationKey(attest)) });

        Assert.Equal(DopplerRefusal.BadCertify, result.Refusal);
        Assert.Equal(1, world.Server.Counts["enrol.refused.badcertify"]);
    }

    [Fact]
    public void A_signing_request_with_an_unknown_algorithm_identifier_is_a_bad_request()
    {
        var tpm = Planned(world);
        var finish = EnrolmentTests.FinishFor(tpm, Begin(world, tpm));
        // id-ecPublicKey (1.2.840.10045.2.1) bent into an identifier no platform knows; the request then no longer loads.
        var csr = Replace(finish.CertificateSigningRequest, [0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01], [0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x7F]);

        Assert.Equal(DopplerRefusal.BadRequest, world.Server.FinishEnrolment(finish with { CertificateSigningRequest = csr }).Refusal);
    }

    [Fact]
    public void Null_parts_of_a_finish_are_refused_not_thrown()
    {
        foreach (var bend in new Func<EnrolFinish, EnrolFinish>[]
        {
            f => f with { Quote = null! },
            f => f with { Quote = f.Quote with { Pcr7Events = null! } },
            f => f with { Quote = f.Quote with { Pcr7 = null! } },
            f => f with { Key = null! },
            f => f with { Reported = null! },
            f => f with { CertificateSigningRequest = null! },
        })
        {
            var tpm = Planned(world);
            var result = world.Server.FinishEnrolment(bend(EnrolmentTests.FinishFor(tpm, Begin(world, tpm))));
            Assert.False(result.Ok);
        }
        Assert.Equal(0, world.Server.Registry.Count);
    }

    // ---- F5: one write for re-enrolment ---------------------------------------------------------------------------------------

    [Fact]
    public void A_plan_withdrawn_while_a_re_enrolment_finishes_leaves_the_old_identity_untouched()
    {
        var store = new WithdrawingStore();
        using var w = new DopplerWorld(store: store);
        var tpm = Planned(w);
        var first = w.Server.FinishEnrolment(EnrolmentTests.FinishFor(tpm, Begin(w, tpm)));
        Assert.True(first.Ok);
        Assert.True(w.Server.AddPlan(new EnrolmentPlan("p-again", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        var finish = EnrolmentTests.FinishFor(tpm, Begin(w, tpm));

        store.WithdrawBeforeNextWrite = tpm.EndorsementKeyId;
        var second = w.Server.FinishEnrolment(finish);

        Assert.Equal(DopplerRefusal.NoApprovedPlan, second.Refusal);
        Assert.False(w.Server.Registry.Find(first.Subject!)!.Revoked);
        Assert.Equal(1, w.Server.Registry.Count);
    }

    [Fact]
    public void A_certificate_issued_for_an_enrolment_that_then_loses_its_plan_goes_on_the_revocation_list()
    {
        var store = new WithdrawingStore();
        var issuer = new RecordingIssuer(world.Authority);
        var server = new DopplerServer(world.Settings, issuer, world.TokenSigner, world.Identity, world.Encoder, world.Authority.Anchors(), world.Audit,
            world.Clock, "milkyway", store);
        var tpm = world.NewTpm();
        Assert.True(server.AddPlan(new EnrolmentPlan("p", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        var begun = server.BeginEnrolment(new EnrolBegin(server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic));
        var finish = EnrolmentTests.FinishFor(tpm, begun.Activation!);

        store.WithdrawBeforeNextWrite = tpm.EndorsementKeyId;
        Assert.Equal(DopplerRefusal.NoApprovedPlan, server.FinishEnrolment(finish).Refusal);

        var issued = Assert.Single(issuer.Issued);
        Assert.True(world.Authority.IsRevoked(issued));
    }

    [Fact]
    public void A_re_enrolment_revokes_the_old_identity_in_the_same_write_that_registers_the_new_one()
    {
        var tpm = Planned(world);
        var first = world.Server.FinishEnrolment(EnrolmentTests.FinishFor(tpm, Begin(world, tpm)));
        Assert.True(world.Server.AddPlan(new EnrolmentPlan("p-again", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));

        var second = world.Server.FinishEnrolment(EnrolmentTests.FinishFor(tpm, Begin(world, tpm)));

        Assert.True(second.Ok);
        var old = world.Server.Registry.Find(first.Subject!)!;
        Assert.True(old.Revoked);
        Assert.True(world.Authority.IsRevoked(first.Certificate!));
        Assert.Equal(1, world.Server.Counts["enrol.revoked"]);
        Assert.Contains(world.Audit.Lines, l => l.Action == "revoke" && l.Target == first.Subject && l.Outcome == "re-enrolment");
    }

    // ---- F3: the agent checks Doppler's answer ---------------------------------------------------------------------------------

    [Fact]
    public void An_agent_refuses_a_device_certificate_of_a_foreign_authority_and_stays_unenrolled()
    {
        using var foreign = new SandboxAuthority("foreign", DopplerWorld.Start.AddMonths(-10));
        var (agent, tpm) = CheckingAgent(world, result => result with
        {
            Certificate = foreign.Issue(TrustRole.Device, result.Subject!, result.Certificate!.PublicKey, DopplerWorld.Start, DopplerWorld.Start.AddDays(30)),
            Chain = [foreign.Issuing],
        });
        agent.Start();

        Assert.False(agent.Enrol());
        Assert.Equal(DopplerRefusal.BadAnswer, agent.LastRefusal);
        Assert.Null(agent.Subject);
        Assert.NotEqual(AgentState.Enrolled, agent.State);
        GC.KeepAlive(tpm);
    }

    [Fact]
    public void An_agent_refuses_an_answer_whose_subject_is_not_the_certificates()
    {
        var (agent, _) = CheckingAgent(world, result => result with { Subject = "dev-0000000000000000" });
        agent.Start();

        Assert.False(agent.Enrol());
        Assert.Equal(DopplerRefusal.BadAnswer, agent.LastRefusal);
    }

    [Fact]
    public void An_agent_refuses_a_certificate_for_another_key_without_an_exception()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherKey = new PublicKey(other);
        var (agent, _) = CheckingAgent(world, result => result with
        {
            Certificate = world.Authority.Issue(TrustRole.Device, result.Subject!, otherKey, DopplerWorld.Start, DopplerWorld.Start.AddDays(30)),
        });
        agent.Start();

        Assert.False(agent.Enrol());
        Assert.Equal(DopplerRefusal.BadAnswer, agent.LastRefusal);
    }

    [Fact]
    public void An_agent_believes_an_honest_answer()
    {
        var (agent, _) = CheckingAgent(world, result => result);
        agent.Start();

        Assert.True(agent.Enrol());
        Assert.Equal(AgentState.Enrolled, agent.State);
    }

    // ---- F6: a kept record that does not hold up means "not enrolled" ---------------------------------------------------------

    [Theory]
    [InlineData("{\"Version\":1,\"Subject\":null,\"Certificate\":null,\"Chain\":null,\"Sequence\":0}")]
    [InlineData("{\"Version\":1,\"Subject\":\"dev-1\",\"Certificate\":\"AAAA\",\"Chain\":null,\"Sequence\":3}")]
    [InlineData("{\"Version\":1,\"Subject\":\"dev-1\",\"Certificate\":\"AAAA\",\"Chain\":[null],\"Sequence\":3}")]
    [InlineData("not json")]
    public void A_garbled_state_file_is_read_as_no_record(string text)
    {
        var folder = Directory.CreateTempSubdirectory("doppler-agent-state-");
        try
        {
            var path = Path.Combine(folder.FullName, "state.json");
            File.WriteAllText(path, text, Encoding.UTF8);
            var loaded = new FileAgentStore(path).Load();
            Assert.True(loaded is null || loaded.Certificate.Length == 3);
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void A_kept_record_with_bytes_that_are_no_certificate_makes_the_loop_enrol_again_instead_of_throwing()
    {
        var tpm = world.NewTpm();
        var store = new KeptRecord(new AgentRecord("dev-0000000000000001", [0x30, 0x03, 0x02, 0x01, 0x01], [], 9));
        var loop = new AgentLoop(tpm, new SimulatedDeviceHost(), new UnreachableGate(), store, world.Authority.Anchors(), world.Clock);

        loop.Step();

        Assert.Equal(AgentOutcome.GateUnreachable, loop.LastOutcome);
        Assert.NotEqual(AgentState.Enrolled, loop.Agent.State);
        Assert.Equal(0UL, loop.Sequence);
    }

    [Fact]
    public void A_kept_record_whose_certificate_is_for_another_key_or_authority_is_not_restored()
    {
        using var foreign = new SandboxAuthority("foreign", DopplerWorld.Start.AddMonths(-10));
        var tpm = world.NewTpm();
        tpm.CreateDeviceKey();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherKeyCertificate = world.Authority.Issue(TrustRole.Device, "dev-0000000000000002", new PublicKey(other), DopplerWorld.Start, DopplerWorld.Start.AddDays(30));
        var foreignSigner = foreign.Issue(TrustRole.Device, "dev-0000000000000003", DopplerWorld.Start, DopplerWorld.Start.AddDays(30));

        foreach (var kept in new[]
        {
            new AgentRecord("dev-0000000000000002", otherKeyCertificate.RawData, [], 4),
            new AgentRecord("dev-0000000000000003", foreignSigner.Certificate.RawData, [foreign.Issuing.RawData], 4),
        })
        {
            var loop = new AgentLoop(tpm, new SimulatedDeviceHost(), new UnreachableGate(), new KeptRecord(kept), world.Authority.Anchors(), world.Clock);
            loop.Step();
            Assert.NotEqual(AgentState.Enrolled, loop.Agent.State);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------

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

    static (DopplerAgent Agent, SimulatedTpm Tpm) CheckingAgent(DopplerWorld w, Func<EnrolmentResult, EnrolmentResult> bend)
    {
        var tpm = Planned(w);
        var agent = new DopplerAgent(tpm, new SimulatedDeviceHost(), new BendingChannel(w.Server, bend), w.Authority.Anchors(), w.Clock);
        return (agent, tpm);
    }

    static byte[] Bytes(int n, byte value) => Enumerable.Repeat(value, n).ToArray();

    static byte[] Replace(byte[] bytes, byte[] find, byte[] with)
    {
        var at = bytes.AsSpan().IndexOf(find);
        Assert.True(at >= 0, "pattern not in the request");
        var copy = bytes.ToArray();
        with.CopyTo(copy, at);
        return copy;
    }

    /// <summary>Doppler's answer as a door that is not genuine, or a broken one, would give it.</summary>
    sealed class BendingChannel(DopplerServer server, Func<EnrolmentResult, EnrolmentResult> bend) : IDopplerChannel
    {
        public byte[]? Challenge() => server.Challenge();
        public BeginResult BeginEnrolment(EnrolBegin request) => server.BeginEnrolment(request);
        public EnrolmentResult FinishEnrolment(EnrolFinish request) => server.FinishEnrolment(request) is { Ok: true } result ? bend(result) : EnrolmentResult.Refused(DopplerRefusal.Unanswered);
        public TokenResult IssueToken(TokenRequest request) => server.IssueToken(request);
    }

    /// <summary>The admin command withdrawing the plan between Doppler's read and its write.</summary>
    sealed class WithdrawingStore : IDopplerStore
    {
        readonly InMemoryDopplerStore inner = new();

        public string? WithdrawBeforeNextWrite { get; set; }

        public DopplerState Read() => inner.Read();

        public DopplerState Update(Func<DopplerState, DopplerState> change)
        {
            if (WithdrawBeforeNextWrite is { } ek)
            {
                WithdrawBeforeNextWrite = null;
                inner.Update(s => s with { Plans = s.Plans.Remove(ek) });
            }
            return inner.Update(change);
        }
    }

    sealed class RecordingIssuer(ICertificateIssuer inner) : ICertificateIssuer
    {
        public List<X509Certificate2> Issued { get; } = [];

        public IReadOnlyList<X509Certificate2> Chain => inner.Chain;

        public X509Certificate2 Issue(TrustRole role, string subject, PublicKey key, DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            var certificate = inner.Issue(role, subject, key, notBefore, notAfter);
            Issued.Add(certificate);
            return certificate;
        }

        public void Revoke(X509Certificate2 certificate) => inner.Revoke(certificate);
    }

    sealed class KeptRecord(AgentRecord record) : IAgentStore
    {
        public AgentRecord? Load() => record;

        public void Save(AgentRecord saved) { }
    }

    sealed class UnreachableGate : IGateConnector
    {
        public DoorChannel OpenDoor() => throw new IOException("unreachable");

        public IGateSession OpenSession(IImSigner device, byte[] token) => throw new IOException("unreachable");
    }
}
