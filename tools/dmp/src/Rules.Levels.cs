using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-014: the levels of the composition tree (constitution C17; DMP chapter 02, "Adding a node" and "Foundation bricks are complete").
// (a) every level-1 node in DMP/tree.txt has an approval line in DMP/tree-approvals.txt (who approved it, where, when), and no stale line;
// (b) protocol and file-format libraries are referenced only by cm.io, so a protocol is spoken in its brick and nowhere else; older uses are
//     listed in DMP/protocol-exceptions.txt, a list that only goes down;
// (c) every level-2 folder of the foundation (cm.io/src/<Brick>/, cm.os/src/<Part>/) has a README.md with a 'Scope:' line, so "complete
//     within its scope" has a written scope to be reviewed against.
// Not checked by the machine: that an approval is real (the review checks The Administrator's and LHC's words) and that a brick really covers its scope.
public static partial class Rules
{
    const string TreeApprovals = "DMP/tree-approvals.txt";
    const string ProtocolExceptions = "DMP/protocol-exceptions.txt";

    static readonly Regex ProtocolPackage = new(
        @"<PackageReference\s+Include\s*=\s*""(?<p>Npgsql[\w.]*|Microsoft\.Data\.SqlClient|System\.Data\.SqlClient|System\.Data\.OleDb|System\.Data\.Odbc|Microsoft\.Data\.Sqlite[\w.]*|MySql\.Data|MySqlConnector|SSH\.NET|Renci\.SshNet|FluentFTP|System\.DirectoryServices\.Protocols|Novell\.Directory\.Ldap[\w.]*|MailKit|MimeKit|Lextm\.SharpSnmpLib|ClosedXML|DocumentFormat\.OpenXml|EPPlus|NPOI|CsvHelper|PdfPig|itext[\w.]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static IEnumerable<Finding> Levels(RepoView repo)
    {
        // (a) level 1 belongs to The Administrator and LHC
        var approvals = Lines(repo.ReadText(TreeApprovals)).Select(l => l.Split('\t')).ToList();
        var nodes = repo.TreeNames();
        foreach (var node in nodes)
        {
            var line = approvals.FirstOrDefault(a => a[0] == node);
            if (line is null) yield return new("DMP-014", TreeApprovals, $"level-1 node '{node}' has no approval line; a new child of CM needs the words of The Administrator and LHC (constitution C17)");
            else if (line.Length < 2 || line[1].Trim().Length < 12) yield return new("DMP-014", TreeApprovals, $"the approval of '{node}' must say where and when The Administrator approved it");
            else if (!line[1].StartsWith("before C17", StringComparison.Ordinal) && !line[1].Contains("LHC", StringComparison.Ordinal))
                yield return new("DMP-014", TreeApprovals, $"the approval of '{node}' must name LHC's words too, or say 'LHC pending' until the first LHC is minted (constitution C17)");
        }
        foreach (var a in approvals.Where(a => !nodes.Contains(a[0], StringComparer.Ordinal)))
            yield return new("DMP-014", TreeApprovals, $"'{a[0]}' is not in DMP/tree.txt (delete the line)");

        // (b) a protocol is spoken in its brick
        var exceptions = Lines(repo.ReadText(ProtocolExceptions)).Select(l => l.Split('\t')).ToList();
        var seen = new HashSet<(string, string)>();
        foreach (var file in repo.Files().Where(f => f.EndsWith(".csproj", StringComparison.Ordinal) && !f.StartsWith("cm.io/", StringComparison.Ordinal)))
            foreach (Match m in ProtocolPackage.Matches(repo.ReadText(file) ?? ""))
            {
                var package = m.Groups["p"].Value;
                if (exceptions.Any(e => e[0] == file && e.Length > 1 && string.Equals(e[1], package, StringComparison.OrdinalIgnoreCase))) { seen.Add((file, package.ToLowerInvariant())); continue; }
                yield return new("DMP-014", file, $"references the protocol or format library '{package}'; protocols are spoken in their cm.io brick and nowhere else (DMP chapter 02)");
            }
        foreach (var e in exceptions)
        {
            if (e.Length < 3 || e[2].Trim().Length < 12) yield return new("DMP-014", ProtocolExceptions, $"'{string.Join(' ', e)}' needs project, package and a reason");
            else if (!seen.Contains((e[0], e[1].ToLowerInvariant()))) yield return new("DMP-014", ProtocolExceptions, $"'{e[0]}' no longer references '{e[1]}'; delete the line");
        }

        // (c) every foundation part writes down its scope
        foreach (var root in new[] { "cm.io/src", "cm.os/src" })
            foreach (var part in repo.Files(root).Where(f => f.EndsWith(".cs", StringComparison.Ordinal) && f.Count(c => c == '/') >= 3)
                                   .Select(f => string.Join('/', f.Split('/').Take(3))).Distinct().Order())
            {
                var readme = repo.ReadText(part + "/README.md");
                if (readme is null || !Regex.IsMatch(readme, @"^Scope: \S", RegexOptions.Multiline))
                    yield return new("DMP-014", part, "a foundation part needs a README.md with a 'Scope:' line: the whole protocol, format or job it covers (DMP chapter 02)");
            }
    }

    static IEnumerable<string> Lines(string? text) =>
        (text ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#');
}
