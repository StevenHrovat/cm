using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Tpm;
using Im;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>
/// The resource limits of Supernova level 3 for Doppler (DMP chapter 06): every field of a door request one byte over its limit is
/// malformed, more PCR 7 events than the limit are malformed, and the table of open challenges and pending enrolments is bounded and
/// fails closed. Slow and many connections are the gate's limits (CM.MilkyWay, GRB-0001), in front of Doppler.
/// </summary>
public sealed class DopplerLimitTests : IDisposable
{
    readonly DopplerWorld world = new();

    public void Dispose() => world.Dispose();

    /// <summary>The fields of a step-2 request in wire order, each with its limit.</summary>
    static readonly (string Name, int Max)[] FinishFields =
    [
        ("credential", DopplerWire.CredentialMax),
        ("device key public area", DopplerWire.TpmPublicMax),
        ("certify attest", DopplerWire.AttestMax),
        ("certify signature", DopplerWire.SignatureMax),
        ("quote attest", DopplerWire.AttestMax),
        ("quote signature", DopplerWire.SignatureMax),
        ("pcr 7", DopplerWire.DigestMax),
    ];

    public static TheoryData<int> FinishFieldIndexes() => new(Enumerable.Range(0, FinishFields.Length));

    [Theory]
    [MemberData(nameof(FinishFieldIndexes))]
    public void A_step_2_field_one_byte_over_its_limit_is_malformed(int oversized)
    {
        var w = new ImWriter().Magic(DopplerWire.RequestMagic, DopplerWire.Version).U8((byte)DoorOp.EnrolFinish).Fixed(new byte[DopplerWire.ChallengeLength], DopplerWire.ChallengeLength);
        for (var i = 0; i < FinishFields.Length; i++)
        {
            var (_, max) = FinishFields[i];
            if (i == 6) w.U16(TpmAlg.Sha256);
            var length = i == oversized ? max + 1 : 1;
            w.Bytes16(new byte[length], max + 1);
        }
        var bytes = w.U16(0).U8(1).Id8(new string('a', DopplerWire.PackageHashLength), ImAlphabet.Name, DopplerWire.PackageHashLength).Bytes16(new byte[8], DopplerWire.CsrMax).ToArray();

        Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(bytes));
        Assert.Null(new DopplerDoor(world.Server).Answer(bytes));
        Assert.Equal(1, world.Server.Counts["door.malformed"]);
    }

    [Fact]
    public void The_same_request_with_every_field_at_its_limit_parses()
    {
        var w = new ImWriter().Magic(DopplerWire.RequestMagic, DopplerWire.Version).U8((byte)DoorOp.EnrolFinish).Fixed(new byte[DopplerWire.ChallengeLength], DopplerWire.ChallengeLength);
        for (var i = 0; i < FinishFields.Length; i++)
        {
            if (i == 6) w.U16(TpmAlg.Sha256);
            w.Bytes16(new byte[FinishFields[i].Max], FinishFields[i].Max);
        }
        var bytes = w.U16(0).U8(1).Id8(new string('a', DopplerWire.PackageHashLength), ImAlphabet.Name, DopplerWire.PackageHashLength).Bytes16(new byte[DopplerWire.CsrMax], DopplerWire.CsrMax).ToArray();

        Assert.NotNull(DopplerWire.ParseRequest(bytes).Finish);
    }

    [Fact]
    public void More_PCR_7_events_than_the_limit_are_malformed_and_never_reach_the_replay()
    {
        var w = new ImWriter().Magic(DopplerWire.RequestMagic, DopplerWire.Version).U8((byte)DoorOp.EnrolFinish).Fixed(new byte[DopplerWire.ChallengeLength], DopplerWire.ChallengeLength);
        for (var i = 0; i < FinishFields.Length; i++)
        {
            if (i == 6) w.U16(TpmAlg.Sha256);
            w.Bytes16(new byte[1], FinishFields[i].Max);
        }
        w.U16(DopplerWire.EventsMax + 1);
        for (var i = 0; i <= DopplerWire.EventsMax; i++) w.U32(TcgEventType.Separator).Bytes16(new byte[32], DopplerWire.DigestMax).Bytes32(new byte[4], ImLimits.DoorMax);
        var bytes = w.U8(1).Id8(new string('a', DopplerWire.PackageHashLength), ImAlphabet.Name, DopplerWire.PackageHashLength).Bytes16(new byte[8], DopplerWire.CsrMax).ToArray();

        Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(bytes));
    }

    [Fact]
    public void Pending_enrolments_count_against_the_open_challenge_cap_and_the_cap_fails_closed()
    {
        using var small = new DopplerWorld(world.Settings with { MaxOpenChallenges = 2 });
        var a = small.NewTpm();
        var b = small.NewTpm();
        foreach (var tpm in new[] { a, b })
        {
            Assert.True(small.Server.AddPlan(new EnrolmentPlan("p-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
            Assert.True(small.Server.BeginEnrolment(new EnrolBegin(small.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Ok);
        }

        // Two enrolments wait for step 2: no third challenge, for anyone, until one finishes or expires.
        Assert.Null(small.Server.Challenge());
        Assert.Equal(1, small.Server.Counts["challenge.refused.full"]);
        small.Clock.Advance(small.Settings.ChallengeLifetime + TimeSpan.FromSeconds(1));
        Assert.NotNull(small.Server.Challenge());
    }

    [Fact]
    public void A_door_payload_at_the_frame_limit_of_garbage_is_malformed_and_counted_without_content()
    {
        var garbage = new byte[ImLimits.DoorMax];
        new Random(5).NextBytes(garbage);

        Assert.Null(new DopplerDoor(world.Server).Answer(garbage));
        Assert.Equal(1, world.Server.Counts["door.malformed"]);
        Assert.Empty(world.Audit.Lines);
    }
}
