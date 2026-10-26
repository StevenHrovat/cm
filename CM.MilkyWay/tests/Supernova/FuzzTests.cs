using CM.MilkyWay.Simulation;
using Xunit;

namespace CM.MilkyWay.Tests.Supernova;

/// <summary>
/// Fuzzing of every parser that takes bytes from another machine, and of the verifier and the handshake behind them (DMP chapter 06,
/// Supernova level 3). <c>tools/verify</c> runs a short seeded campaign per target and replays the kept corpus
/// (<c>Supernova/corpus/&lt;target&gt;/*.bin</c>). The long run is <c>tools/fuzz</c>; what it finds is added to the corpus.
/// </summary>
[Trait("Category", "im")]
public sealed class FuzzTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Iterations per target in <c>tools/verify</c>: enough to reach every outcome class, short enough to keep the gate fast.</summary>
    const int Short = 2000;

    public static TheoryData<FuzzTarget> Targets() => new(Enum.GetValues<FuzzTarget>());

    [Theory]
    [MemberData(nameof(Targets))]
    public void A_short_seeded_campaign_breaks_no_oracle(FuzzTarget target)
    {
        using var world = new SandboxWorld(Start);
        var report = new ImFuzzer(world, seed: 20261102 + (int)target).Campaign(target, target is FuzzTarget.Handshake ? Short / 10 : Short, Corpus(target));

        Assert.True(report.Findings.Count == 0, $"{target}: " + string.Join("; ", report.Findings.Select(f => f.Oracle).Distinct()));
        // The handshake's changed responses all end in the refused frame (a session past the knock never goes quiet), so one outcome is right there.
        Assert.True(report.Outcomes.Count >= (target is FuzzTarget.Handshake ? 1 : 2), $"{target}: the campaign reached only {string.Join(", ", report.Outcomes.Keys)}");
        Assert.DoesNotContain(report.Outcomes.Keys, k => k is "verified" or "established");
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void The_kept_corpus_replays_without_a_finding(FuzzTarget target)
    {
        using var world = new SandboxWorld(Start);
        var fuzzer = new ImFuzzer(world, seed: 1);
        var findings = new List<FuzzFinding>();
        foreach (var input in Corpus(target)) fuzzer.Run(target is FuzzTarget.Handshake ? FuzzTarget.Frames : target, input, original: null, findings);
        Assert.Empty(findings);
    }

    [Fact]
    public void The_corpus_holds_inputs_for_every_parser_target()
    {
        foreach (var target in new[] { FuzzTarget.Token, FuzzTarget.Envelope, FuzzTarget.Heartbeat, FuzzTarget.Frames })
            Assert.True(Corpus(target).Count >= 3, $"the corpus for {target} is missing or nearly empty");
    }

    [Fact]
    public void The_same_seed_gives_the_same_mutations()
    {
        using var world = new SandboxWorld(Start);
        var input = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        var a = new ImFuzzer(world, 7);
        var b = new ImFuzzer(world, 7);
        for (var i = 0; i < 50; i++) Assert.Equal(a.Mutate(input), b.Mutate(input));
    }

    /// <summary>The kept inputs of a target. The verifier's and the handshake's inputs depend on keys made per run, so they have none.</summary>
    static IReadOnlyList<byte[]> Corpus(FuzzTarget target)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Supernova", "corpus", target.ToString().ToLowerInvariant());
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.bin").Order(StringComparer.Ordinal).Select(File.ReadAllBytes).ToArray() : [];
    }
}
