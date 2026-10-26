using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Im;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Supernova;

/// <summary>What the Doppler fuzzer feeds. The first five take bytes from another machine; the TCG log is the agent's own firmware's.</summary>
public enum DopplerFuzzTarget
{
    /// <summary>Any door payload (every op), through <see cref="DopplerDoor"/>: answered or refused, never a failure inside Doppler.</summary>
    Door,
    /// <summary>A valid step-2 request with one mutation, through the door: never an enrolment.</summary>
    Finish,
    TpmPublic,
    TpmAttest,
    TpmSignature,
    TcgLog,
}

/// <summary>
/// Fuzzing of every parser of Doppler that takes bytes from another machine, and of the enrolment decision behind the door (DMP chapter
/// 06, Supernova level 3). <c>tools/verify</c> runs a short seeded campaign per target and replays the kept corpus
/// (<c>Doppler/Supernova/corpus/&lt;target&gt;/*.bin</c>). The oracles: a parser throws only its format exception; the door never counts
/// <c>door.failed</c> (an exception escaped the decision) and never answers a changed step-2 request with an enrolment.
/// </summary>
public sealed class DopplerFuzzTests : IDisposable
{
    const int Short = 1500;
    const int ShortFinish = 150;

    readonly DopplerWorld world = new();
    readonly SimulatedTpm tpm;
    readonly DopplerDoor door;

    public DopplerFuzzTests()
    {
        tpm = world.NewTpm();
        Assert.True(world.Server.AddPlan(new EnrolmentPlan("p-fuzz", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        door = new DopplerDoor(world.Server);
    }

    public void Dispose() => world.Dispose();

    public static TheoryData<DopplerFuzzTarget> Targets() => new(Enum.GetValues<DopplerFuzzTarget>());

    [Theory]
    [MemberData(nameof(Targets))]
    public void A_short_seeded_campaign_breaks_no_oracle(DopplerFuzzTarget target)
    {
        var random = new Random(20261025 + (int)target);
        var seeds = Seeds(target);
        var findings = new List<string>();
        var count = target == DopplerFuzzTarget.Finish ? ShortFinish : Short;
        for (var i = 0; i < count; i++)
        {
            var original = target == DopplerFuzzTarget.Finish ? FreshFinish() : seeds[random.Next(seeds.Count)];
            var mutated = Mutate(random, original);
            // A mutation can give back the original bytes (a byte set to the value it had, a bit flipped twice). An honest step 2 enrols and
            // uses up the one plan, so every later Begin of the campaign would be refused; the unchanged input is no test anyway.
            if (target == DopplerFuzzTarget.Finish && mutated.AsSpan().SequenceEqual(original)) continue;
            Run(target, mutated, original, findings);
        }
        foreach (var input in Corpus(target)) Run(target, input, null, findings);

        Assert.True(findings.Count == 0, $"{target}: " + string.Join("; ", findings.Distinct().Take(5)));
        Assert.False(world.Server.Counts.ContainsKey("door.failed"), "an exception escaped the door's decision");
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void The_kept_corpus_replays_without_a_finding(DopplerFuzzTarget target)
    {
        var corpus = Corpus(target);
        Assert.True(corpus.Count >= 3, $"the corpus for {target} is missing or nearly empty");
        var findings = new List<string>();
        foreach (var input in corpus) Run(target, input, null, findings);
        Assert.Empty(findings);
    }

    /// <summary>One input against one target. <paramref name="original"/> is the unmutated input when there is one.</summary>
    void Run(DopplerFuzzTarget target, byte[] input, byte[]? original, List<string> findings)
    {
        try
        {
            switch (target)
            {
                case DopplerFuzzTarget.Door:
                    door.Answer(input);
                    break;
                case DopplerFuzzTarget.Finish:
                    var answer = door.Answer(input);
                    var changed = original is null || !input.AsSpan().SequenceEqual(original);
                    if (answer is not null && changed && (original is null || !OnlyEventTypesDiffer(original, input)))
                        findings.Add("a changed enrolment request was answered with an enrolment");
                    // An enrolment uses up the plan; a new one keeps the campaign's later steps meaningful.
                    if (answer is not null) Assert.True(world.Server.AddPlan(new EnrolmentPlan("p-fuzz", tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
                    break;
                case DopplerFuzzTarget.TpmPublic:
                    try { TpmEvidence.CheckAttestationKey(input, allowSha1: true); TpmPublic.Parse(input); } catch (TpmFormatException) { }
                    break;
                case DopplerFuzzTarget.TpmAttest:
                    try { TpmAttest.Parse(input); } catch (TpmFormatException) { }
                    break;
                case DopplerFuzzTarget.TpmSignature:
                    try { TpmSignature.Parse(input); } catch (TpmFormatException) { }
                    break;
                case DopplerFuzzTarget.TcgLog:
                    try { TcgLog.EventsOf(input, 7, TpmAlg.Sha256); } catch (TpmFormatException) { }
                    break;
            }
        }
        catch (Exception e)
        {
            findings.Add($"{e.GetType().Name} escaped");
        }
    }

    List<byte[]> Seeds(DopplerFuzzTarget target)
    {
        var activation = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Activation!;
        var finish = EnrolmentTests.FinishFor(tpm, activation);
        return target switch
        {
            DopplerFuzzTarget.Door =>
            [
                DopplerWire.ChallengeRequest(),
                DopplerWire.BeginRequest(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)),
                DopplerWire.FinishRequest(finish),
                DopplerWire.TokenRequest(new TokenRequest("dev-0000", world.Server.Challenge()!, new byte[64])),
            ],
            DopplerFuzzTarget.TpmPublic => [tpm.AttestationKeyPublic, finish.Key.DeviceKeyPublic],
            DopplerFuzzTarget.TpmAttest => [finish.Key.Attest, finish.Quote.Attest],
            DopplerFuzzTarget.TpmSignature => [finish.Key.Signature, finish.Quote.Signature],
            DopplerFuzzTarget.TcgLog => [SimulatedLog()],
            _ => [],
        };
    }

    /// <summary>
    /// True when the two step-2 requests differ only in the types of PCR 7 events. The TPM measures an event's data, not its type, so the type is
    /// the agent's word, and since GRB-0003 F1 no decision rests on it: a separator and the SecureBoot variable are known by their data too. A
    /// change of types alone must therefore leave the decision as it was, and an enrolment after one is not a finding.
    /// </summary>
    static bool OnlyEventTypesDiffer(byte[] original, byte[] changed)
    {
        EnrolFinish a, b;
        try { (a, b) = (DopplerWire.ParseRequest(original).Finish!, DopplerWire.ParseRequest(changed).Finish!); }
        catch (ImFormatException) { return false; }
        if (b is null || a.Quote.Pcr7Events.Count != b.Quote.Pcr7Events.Count) return false;
        var withNewTypes = a with { Quote = a.Quote with { Pcr7Events = [.. a.Quote.Pcr7Events.Zip(b.Quote.Pcr7Events, (x, y) => x with { Type = y.Type })] } };
        return DopplerWire.FinishRequest(withNewTypes).AsSpan().SequenceEqual(changed);
    }

    byte[] FreshFinish()
    {
        var activation = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, tpm.EndorsementKeySpki, tpm.AttestationKeyPublic)).Activation!;
        return DopplerWire.FinishRequest(EnrolmentTests.FinishFor(tpm, activation));
    }

    static byte[] SimulatedLog() => TcgLog.Build(
    [
        (7, TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1])),
        (7, TcgEventType.Separator, [0, 0, 0, 0]),
        (0, TcgEventType.Separator, [0, 0, 0, 0]),
    ]);

    /// <summary>One to four mutations: a bit flip, a byte set to an edge value, a cut, an insert, a duplicated slice or a length field made large.</summary>
    internal static byte[] Mutate(Random random, byte[] input)
    {
        var bytes = new List<byte>(input);
        var rounds = 1 + random.Next(4);
        for (var r = 0; r < rounds; r++)
        {
            if (bytes.Count == 0) { bytes.Add((byte)random.Next(256)); continue; }
            var at = random.Next(bytes.Count);
            switch (random.Next(6))
            {
                case 0: bytes[at] ^= (byte)(1 << random.Next(8)); break;
                case 1: bytes[at] = (byte)(random.Next(4) switch { 0 => 0x00, 1 => 0xFF, 2 => 0x7F, _ => 0x80 }); break;
                case 2: bytes.RemoveRange(at, bytes.Count - at); break;
                case 3: bytes.InsertRange(at, Enumerable.Range(0, 1 + random.Next(8)).Select(_ => (byte)random.Next(256))); break;
                case 4:
                    var length = Math.Min(bytes.Count - at, 1 + random.Next(16));
                    bytes.InsertRange(at, bytes.GetRange(at, length));
                    break;
                default:
                    if (at + 1 < bytes.Count) { bytes[at] = 0xFF; bytes[at + 1] = 0xFF; }
                    break;
            }
        }
        return [.. bytes];
    }

    static IReadOnlyList<byte[]> Corpus(DopplerFuzzTarget target)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Doppler", "Supernova", "corpus", target.ToString().ToLowerInvariant());
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.bin").Order(StringComparer.Ordinal).Select(File.ReadAllBytes).ToArray() : [];
    }
}
