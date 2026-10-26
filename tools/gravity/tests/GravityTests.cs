using Im;
using CM.MilkyWay;
using CM.MilkyWay.Simulation;
using Xunit;

namespace Cm.Gravity.Tests;

/// <summary>What of Gravity's gate runs without Bend: the mutants still fit the model, the probe reads facts right, the encoding is exact.</summary>
public sealed class GravityTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    readonly SandboxWorld world = new(Start);

    public void Dispose() => world.Dispose();

    static string Section()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CM.slnx"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("CM.slnx not found"), "CM.BlackHole", "gravity", "C02");
    }

    MilkyWaySettingsView View() => new(world.Settings.MaxFrameBytes, world.Settings.MaxPayloadBytes, (long)world.Settings.ClockSkew.TotalMilliseconds,
        (long)world.Settings.TokenMaxLifetime.TotalMilliseconds, world.Settings.ReplayCapacity);

    Facts Probe(SimulatedDevice device, string? session = null, byte[]? token = null)
    {
        var now = world.Clock.GetUtcNow();
        var frame = ImEnvelopeCodec.Seal(ImKinds.Heartbeat, device.NextHeartbeat().Encode(), token ?? device.Token, device.Key, now, world.Settings.MaxPayloadBytes);
        return FactProbe.Probe(frame, session, now, View(), world.Anchors, world.Enrolment);
    }

    [Fact]
    public void Every_mutant_still_applies_to_the_model()
    {
        var model = File.ReadAllText(Path.Combine(Section(), "model.bend"));
        var mutants = Mutant.Load(Path.Combine(Section(), "mutants.json"));
        Assert.NotEmpty(mutants);
        Assert.Equal(mutants.Count, mutants.Select(m => m.Id).Distinct().Count());
        foreach (var m in mutants)
        {
            Assert.NotEqual(model, m.Apply(model));
            Assert.Contains("def main() -> Bool:", m.Witness, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_device_s_own_heartbeat_has_every_fact_passing()
    {
        var f = Probe(world.EnrolDevice());
        Assert.Equal((false, Parse.POk, Parse.POk, Chain.COk, true, Enrol.EOk, Session.SNone, true, true, Parse.POk, (string?)null),
            (f.Oversize, f.Env, f.Tok, f.Chain, f.TokSig, f.Enrol, f.Session, f.MsgSig, f.Heartbeat, f.Payload, f.Outside));
        Assert.Contains(Perm.HeartbeatSend, f.Perms);
    }

    [Fact]
    public void The_probe_reads_revocation_session_and_signer_on_their_own()
    {
        var revoked = world.EnrolDevice();
        world.Enrolment.Revoke(revoked.Subject);
        Assert.Equal(Enrol.ERevoked, Probe(revoked).Enrol);

        var device = world.EnrolDevice();
        Assert.Equal(Session.SSame, Probe(device, device.Subject).Session);
        Assert.Equal(Session.SOther, Probe(device, "d-someone-else").Session);

        var foreign = world.Foreign.Issue(Im.Trust.TrustRole.ImTokenSigner, "foreign-signer", Start.AddDays(-1), Start.AddDays(1));
        var f = Probe(device, token: world.IssueToken(device, signer: foreign));
        Assert.Equal(Chain.CUntrusted, f.Chain);
        Assert.Equal(Enrol.EOk, f.Enrol); // read although rule 4 already refuses: nothing in the probe stops early
    }

    [Fact]
    public void Nats_are_written_exactly_or_not_at_all()
    {
        Assert.Equal("4294967295n", BendText.Nat(uint.MaxValue));
        Assert.Equal("Nat.add(Nat.mul(Nat.mul(1n, 65536n), 65536n), 5n)", BendText.Nat((1L << 32) + 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => BendText.Nat(BendText.Span));
        Assert.Throws<ArgumentOutOfRangeException>(() => BendText.Nat(-1));
        var text = new BendText(1000);
        Assert.True(text.Fits(1000));
        Assert.False(text.Fits(999));
    }

    [Fact]
    public void The_model_s_printed_answers_parse()
    {
        Assert.Equal([0, 3, 19], Conform.ParseCodes("[0n, 3n, 19n]"));
        Assert.Null(Conform.ParseCodes("[0n, 20n]"));
        Assert.Null(Conform.ParseCodes("SOME PROOFS FAIL"));
    }
}
