using System.Text;
using CM.BlackHole.Doppler;
using Im;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler;

/// <summary>Doppler's register on disk and the canonical door payloads (lane L4b goal 1).</summary>
public sealed class StoreAndWireTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("doppler-store-").FullName;
    readonly DopplerWorld world;

    public StoreAndWireTests() => world = new DopplerWorld(store: new FileDopplerStore(folder));

    public void Dispose()
    {
        world.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void The_register_file_holds_plans_machines_and_token_ids_and_no_key_or_token()
    {
        var (agent, tpm, _) = world.NewMachine();
        agent.Start();
        Assert.True(agent.Enrol());
        Assert.True(agent.RefreshToken());
        var second = world.NewTpm();
        world.Server.AddPlan(new EnrolmentPlan("plan-two", second.EndorsementKeyId, EnrolledRole.Test, ["admin-a", "admin-b"]));

        var text = File.ReadAllText(Path.Combine(folder, FileDopplerStore.FileName), Encoding.UTF8);

        Assert.Contains(agent.Subject!, text, StringComparison.Ordinal);
        Assert.Contains(second.EndorsementKeyId, text, StringComparison.Ordinal);
        Assert.Contains(tpm.EndorsementKeyId, text, StringComparison.Ordinal); // the register keeps the id; its plan is used up
        Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(agent.Token!.Encoded), text, StringComparison.Ordinal);
        Assert.Contains(agent.Token.TokenId, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_store_on_the_same_folder_sees_every_write_of_the_first()
    {
        var other = new FileDopplerStore(folder);
        var tpm = world.NewTpm();

        Assert.True(world.Server.AddPlan(new EnrolmentPlan("plan-a", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        Assert.True(other.Read().Plans.ContainsKey(tpm.EndorsementKeyId));

        Assert.True(DopplerAdmin.RemovePlan(other, tpm.EndorsementKeyId));
        Assert.False(new FileDopplerStore(folder).Read().Plans.ContainsKey(tpm.EndorsementKeyId));
    }

    [Fact]
    public void A_plan_with_an_endorsement_key_id_that_is_not_a_sha256_is_refused()
    {
        Assert.False(world.Server.AddPlan(new EnrolmentPlan("p", "not-a-key-id", EnrolledRole.Test, ["admin-a", "admin-b"])));
        Assert.False(world.Server.AddPlan(new EnrolmentPlan("p", new string('a', 63), EnrolledRole.Test, ["admin-a", "admin-b"])));
    }

    [Fact]
    public void Door_requests_round_trip_and_reject_trailing_bytes_and_unknown_operations()
    {
        var token = new TokenRequest("dev-0011223344556677", DopplerWorld.RandomBytes(32), DopplerWorld.RandomBytes(64));
        var bytes = DopplerWire.TokenRequest(token);

        var parsed = DopplerWire.ParseRequest(bytes);
        Assert.Equal(DoorOp.Token, parsed.Op);
        Assert.Equal(token.Subject, parsed.Token!.Subject);
        Assert.Equal(token.Challenge, parsed.Token.Challenge);

        Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(new byte[][] { bytes, [0] }.SelectMany(b => b).ToArray()));
        Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(bytes[..^1]));
        var unknown = (byte[])bytes.Clone();
        unknown[4] = 9;
        Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(unknown));
        var future = (byte[])bytes.Clone();
        future[3] = 1;
        Assert.Equal(ImFormatError.UnknownVersion, Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(future)).Error);
    }

    [Fact]
    public void An_enrolment_survives_the_wire_both_steps_and_still_enrols()
    {
        var tpm = world.NewTpm();
        world.Server.AddPlan(new EnrolmentPlan("p", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"]));
        var begin = new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic);

        var begun = world.Server.BeginEnrolment(DopplerWire.ParseRequest(DopplerWire.BeginRequest(begin)).Begin!);
        var activation = DopplerWire.ParseActivationAnswer(DopplerWire.ActivationAnswer(begun.Activation!));
        var finish = EnrolmentTests.FinishFor(tpm, activation);
        var result = world.Server.FinishEnrolment(DopplerWire.ParseRequest(DopplerWire.FinishRequest(finish)).Finish!);

        Assert.True(result.Ok, result.Refusal.ToString());
        var (subject, certificate, chain) = DopplerWire.ParseEnrolAnswer(DopplerWire.EnrolAnswer(result));
        Assert.Equal(result.Subject, subject);
        Assert.Equal(result.Certificate!.RawData, certificate.RawData);
        Assert.Single(chain);
    }

    [Fact]
    public void Every_cut_of_an_enrolment_request_is_malformed_and_no_flipped_byte_escapes_as_anything_but_a_refusal()
    {
        var tpm = world.NewTpm();
        world.Server.AddPlan(new EnrolmentPlan("p", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"]));
        var begun = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic));
        var bytes = DopplerWire.FinishRequest(EnrolmentTests.FinishFor(tpm, begun.Activation!));

        for (var cut = 0; cut < bytes.Length; cut++)
            Assert.Throws<ImFormatException>(() => DopplerWire.ParseRequest(bytes.AsMemory(0, cut)));

        // A fixed seed: the same flips on every run (DMP-010). Each flip is in a fresh, otherwise valid enrolment, and is either
        // malformed on the wire or refused by Doppler; nothing in the request can change without the enrolment failing.
        var random = new Random(4242);
        for (var i = 0; i < 60; i++)
        {
            var activation = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Activation!;
            var flipped = DopplerWire.FinishRequest(EnrolmentTests.FinishFor(tpm, activation));
            flipped[random.Next(flipped.Length)] ^= (byte)(1 << random.Next(8));
            DoorRequest request;
            try { request = DopplerWire.ParseRequest(flipped); }
            catch (ImFormatException) { continue; }
            Assert.False(world.Server.FinishEnrolment(request.Finish!).Ok);
        }
    }
}
