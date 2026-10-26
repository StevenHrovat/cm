using System.Globalization;
using System.Security.Cryptography;
using CM.MilkyWay.Simulation;

// tools/fuzz: the long fuzz run (DMP chapter 06, Supernova levels 3 and 4).
//   dotnet run --project tools/fuzz/src/Cm.Fuzz.csproj -- [--iterations N] [--seed S] [--target Token|Envelope|...|all] [--corpus DIR] [--keep]
// Every campaign starts from the kept corpus; with --keep, inputs that reach an outcome the campaign had not seen and every input that
// breaks an oracle are written to the corpus (file name = SHA-256 of the bytes, so a rerun adds nothing twice). Exit 1 on any finding.
// The sandbox world starts at a fixed time, so the only randomness is the seed and the keys the world makes.

var iterations = 100_000;
var seed = 20261102;
var targets = Enum.GetValues<FuzzTarget>();
var corpusRoot = Path.Combine("CM.MilkyWay", "tests", "Supernova", "corpus");
var keepNew = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--iterations": iterations = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--seed": seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--target": targets = args[++i] == "all" ? targets : [Enum.Parse<FuzzTarget>(args[i], ignoreCase: true)]; break;
        case "--corpus": corpusRoot = args[++i]; break;
        case "--keep": keepNew = true; break;
        default: Console.Error.WriteLine("unknown argument " + args[i]); return 2;
    }
}

var start = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
var failed = false;
foreach (var target in targets)
{
    using var world = new SandboxWorld(start);
    // The verifier's and the handshake's inputs carry keys made for this run, so only the parser targets keep a corpus.
    var keeps = target is not (FuzzTarget.Verifier or FuzzTarget.Handshake);
    var folder = Path.Combine(corpusRoot, target.ToString().ToLowerInvariant());
    var corpus = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.bin").Order(StringComparer.Ordinal).Select(File.ReadAllBytes).ToList() : [];
    var fuzzer = new ImFuzzer(world, seed + (int)target);
    var added = 0;
    if (keepNew && keeps)
    {
        Directory.CreateDirectory(folder);
        foreach (var s in fuzzer.Seeds(target)) added += Keep(folder, s);
    }
    var report = fuzzer.Campaign(target, target is FuzzTarget.Handshake ? Math.Max(1, iterations / 10) : iterations, corpus,
        keepNew && keeps ? (_, input) => added += Keep(folder, input) : null);
    foreach (var finding in report.Findings)
    {
        failed = true;
        if (keepNew && keeps) Keep(folder, finding.Input);
    }
    Console.WriteLine($"{target,-10} {report.Iterations,9} inputs  {report.Outcomes.Count,3} outcomes  {report.Findings.Count,3} findings  {added,3} kept");
    foreach (var oracle in report.Findings.Select(f => f.Oracle).Distinct()) Console.WriteLine("    FINDING " + oracle);
}
Console.WriteLine(failed ? "fuzz: FINDINGS" : "fuzz: OK");
return failed ? 1 : 0;

static int Keep(string folder, byte[] input)
{
    var path = Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(input))[..16] + ".bin");
    if (File.Exists(path)) return 0;
    File.WriteAllBytes(path, input);
    return 1;
}
