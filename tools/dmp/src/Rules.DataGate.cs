using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-013: company data moves to general testing only with its proof (constitution C16; templates/data-gate/README.md). The register is
// DMP/data-features.txt, one feature per line: id<TAB>stage<TAB>paths (comma-separated prefixes)<TAB>handling document<TAB>GRB report.
// The register spans the suite: a path in another repository is written <repository>:<path> or <repository>#<pull request> and
// is not checked here (this checkout cannot see it); the detector (a) runs on this repository only, until the modules move into it.
// stage is "it" (IT and trusted advisors) or "general" (general testing). The rule checks:
// (a) code that reaches company data (a data package in a project, or a cm.io data brick used outside cm.io) lies under the paths of a
//     registered feature, so nothing that touches company data is outside the register;
// (b) a feature at stage "general" names both documents, both exist, both are pages built from the artifact template (they keep its
//     provenance line), and the GRB report names a GRB scope file that exists under CM.BlackHole/docs/grb/.
// Not checked by the machine: that the documents are true and the GRB result holds. That is the review of the pull request that moves the stage.
public static partial class Rules
{
    const string DataRegister = "DMP/data-features.txt";
    const string TemplateProvenance = "templates/map/README.md";   // the provenance comment every page copied from the artifact template keeps

    static readonly Regex DataPackage = new(
        @"<PackageReference\s+Include\s*=\s*""(?<p>Npgsql[\w.]*|System\.Data\.OleDb|Microsoft\.Data\.SqlClient|System\.Data\.SqlClient|Microsoft\.Graph[\w.]*|Microsoft\.Exchange[\w.]*|MailKit|MimeKit|ClosedXML|DocumentFormat\.OpenXml)""",
        RegexOptions.Compiled);

    static readonly Regex DataBrickUse = new(@"^\s*using\s+(?<b>cm\.io\.(?:Database|Spreadsheet)[\w.]*)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

    static readonly Regex ScopeFileRef = new(@"GRB-\d{4}-[\w.-]+\.md", RegexOptions.Compiled);

    static bool IsElsewhere(string path) => Regex.IsMatch(path, @"^[a-z][\w-]*[:#]");

    sealed record DataFeature(string Id, string Stage, string[] Paths, string Handling, string Report);

    static IEnumerable<Finding> DataGate(RepoView repo)
    {
        var features = new List<DataFeature>();
        var text = repo.ReadText(DataRegister);
        if (text is null) { yield return new("DMP-013", DataRegister, "missing: the register of features that touch company data"); yield break; }
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#'))
        {
            var f = line.Split('\t');
            if (f.Length != 5) { yield return new("DMP-013", DataRegister, $"'{line}' is not id<TAB>stage<TAB>paths<TAB>handling<TAB>report (write '-' for a document that does not exist yet)"); continue; }
            if (f[1] is not ("it" or "general")) { yield return new("DMP-013", DataRegister, $"feature '{f[0]}': stage '{f[1]}' is neither 'it' nor 'general'"); continue; }
            var paths = f[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paths.Where(p => !IsElsewhere(p) && !repo.Exists(p))) yield return new("DMP-013", DataRegister, $"feature '{f[0]}': path '{p}' does not exist");
            features.Add(new(f[0], f[1], paths, f[3], f[4]));
        }

        // (a) everything that reaches company data is registered
        var sources = repo.Files().Where(p => (p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".cs", StringComparison.Ordinal))
                                             && !p.StartsWith("cm.io/", StringComparison.Ordinal) && !p.StartsWith("tools/dmp/", StringComparison.Ordinal)
                                             && !p.Contains("/tests/", StringComparison.Ordinal));
        foreach (var file in sources)
        {
            var code = repo.ReadText(file) ?? "";
            var hit = file.EndsWith(".csproj", StringComparison.Ordinal) ? DataPackage.Match(code) is { Success: true } m ? m.Groups["p"].Value : null
                                                                          : DataBrickUse.Match(code) is { Success: true } u ? u.Groups["b"].Value : null;
            if (hit is null) continue;
            if (!features.Any(f => f.Paths.Any(p => file == p || file.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal))))
                yield return new("DMP-013", file, $"reaches company data ({hit}) but no feature in {DataRegister} covers it; register it at stage 'it' (constitution C16)");
        }

        // (b) general testing needs the document pair
        foreach (var f in features.Where(f => f.Stage == "general"))
        {
            foreach (var (what, path) in new[] { ("handling document", f.Handling), ("GRB report", f.Report) })
            {
                if (IsElsewhere(path)) continue;                                              // a page in another repository is reviewed there
                if (path == "-" || !repo.Exists(path)) { yield return new("DMP-013", DataRegister, $"feature '{f.Id}' is at stage 'general' but its {what} is missing ('{path}')"); continue; }
                var page = repo.ReadText(path) ?? "";
                if (!path.EndsWith(".html", StringComparison.Ordinal) || !page.Contains(TemplateProvenance, StringComparison.Ordinal))
                    yield return new("DMP-013", path, $"the {what} of '{f.Id}' is not a page built from the artifact template (copy templates/map/map-template.html; keep its provenance line)");
                if (what == "GRB report")
                {
                    var scope = ScopeFileRef.Match(page);
                    if (!scope.Success || !repo.Exists("CM.BlackHole/docs/grb/" + scope.Value))
                        yield return new("DMP-013", path, $"the GRB report of '{f.Id}' names no scope file under CM.BlackHole/docs/grb/ (a GRB run happens only under a scope file, DMP chapter 06)");
                }
            }
        }
    }
}
