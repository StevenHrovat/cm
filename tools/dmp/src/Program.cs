using Cm.Dmp;

// Usage: dmp check [repository-root]
//        dmp pin <repository-root> <vendor-name> <source> <commit> <version>   (writes vendor/<name>/PIN from the files now in the folder)
// Optional: CM_DMP_SCRUB_LIST names a file with one forbidden value per line (supplied by the pipeline, never committed).
if (args.Length == 6 && args[0] == "pin")
{
    var folder = $"vendor/{args[2]}";
    var hash = Rules.VendorHash(new RepoView(args[1]), folder);
    File.WriteAllText(Path.Combine(args[1], folder, "PIN"), $"source\t{args[3]}\ncommit\t{args[4]}\nversion\t{args[5]}\nsha256\t{hash}\n");
    Console.WriteLine($"{folder}/PIN written: {hash}");
    return 0;
}
if (args.Length == 0 || args[0] != "check")
{
    Console.Error.WriteLine("usage: dmp check [repository-root] | dmp pin <repository-root> <vendor-name> <source> <commit> <version>");
    return 2;
}

// On a pull request GitHub sets GITHUB_BASE_REF and GITHUB_EVENT_PATH; the base commit (pull_request.base.sha) feeds the ratchets of DMP-020.
var repo = RepoView.WithGitBase(args.Length > 1 ? args[1] : ".", Environment.GetEnvironmentVariable("CM_DMP_SCRUB_LIST"),
    Environment.GetEnvironmentVariable("GITHUB_BASE_REF"), Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH"));
var failures = 0;
Console.WriteLine($"dmp: {Rules.All.Count} rules");
foreach (var rule in Rules.All)
{
    var findings = rule.Check(repo).ToList();
    Console.WriteLine($"  {(findings.Count == 0 ? "PASS" : "FAIL")} {rule.Id} {rule.Title}");
    foreach (var f in findings) Console.WriteLine($"       {f.Path}: {f.Message}");
    failures += findings.Count;
}
if (repo.ScrubList.Count == 0)
    Console.WriteLine("  NOTE DMP-006 ran without a scrub list (CM_DMP_SCRUB_LIST not set); only the shape checks applied");
foreach (var section in Rules.UnenforcedStableSections(repo))
    Console.WriteLine($"  NOTE stable but enforced by prose only: {section}");
Console.WriteLine(failures == 0 ? "dmp: OK" : $"dmp: FAILED ({failures} findings)");
return failures == 0 ? 0 : 1;
