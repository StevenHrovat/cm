using System.Globalization;
using Cm.Gravity;

// tools/gravity: Gravity's gate for DMP laws (CM.BlackHole/docs/gravity.md).
//   dotnet run --project tools/gravity/src/Cm.Gravity.csproj -- check [repo]
//       every section under CM.BlackHole/gravity: `bend PROOF.bend --verdict` (Bend's checker, then the kernel proven in Lean),
//       then every mutant in mutants.json must build, break a proof, and be told apart from the model by its witness
//   dotnet run --project tools/gravity/src/Cm.Gravity.csproj -- conform [--cases N] [--seed S] [repo]
//       the C2 model, compiled to JavaScript, against ImVerifier on a deterministic sandbox campaign
// Bend comes from CM_BEND (a command line) or `bend` on the PATH, Node from CM_NODE or `node`; without them it prints that the step
// was skipped and exits 0, as tools/verify does for PowerShell. Exit 1 when a proof fails, a mutant is not caught or a case differs.

var command = args.Length > 0 ? args[0] : "";
var cases = 3000;
var seed = 20261025;
var repo = ".";
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--cases": cases = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--seed": seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        default: repo = args[i]; break;
    }
}
if (command is not ("check" or "conform")) { Console.Error.WriteLine("usage: gravity check|conform [--cases N] [--seed S] [repo]"); return 2; }

var bend = Bend.Find();
if (bend is null) { Console.WriteLine("gravity: skipped (no bend or node here; set CM_BEND and CM_NODE, CM.BlackHole/docs/gravity.md)"); return 0; }
var root = Path.Combine(Path.GetFullPath(repo), "CM.BlackHole", "gravity");
var sections = Directory.Exists(root) ? Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, "PROOF.bend"))).Order(StringComparer.Ordinal).ToList() : [];
if (sections.Count == 0) { Console.Error.WriteLine("gravity: no section with a PROOF.bend under " + root); return 1; }

var ok = true;
if (command == "check")
    foreach (var section in sections) ok &= Check.Section(bend, section, Console.Out);
else
    ok = Conform.Run(bend, sections.Single(s => Path.GetFileName(s) == "C02"), cases, seed, Console.Out);
Console.WriteLine(ok ? "gravity: OK" : "gravity: FAILED");
return ok ? 0 : 1;
