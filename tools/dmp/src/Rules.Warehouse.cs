using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-016: a warehouse serves an IM request only when it is secure and meant to be served (constitution C19; DMP/warehouses.txt).
// (a) the register lists every information warehouse with its tree node (a folder here, an outside reference such as other-repo#…, or '-'
//     while it is not built) and whether it serves IM yet;
// (b) a warehouse node whose source uses Im must say 'yes', so serving can never start unregistered;
// (c) a warehouse that serves IM has the test <node>/tests/Im/DualSignatureTests.cs, which proves it refuses a request that lacks either
//     chain's signature or carries a grant the other chain did not sign;
// (d) DMP 04 keeps the section that says how a warehouse checks both chains itself ("Serving at a warehouse").
// While no warehouse serves IM the rule checks only (a), (b) and (d).
public static partial class Rules
{
    const string WarehouseRegister = "DMP/warehouses.txt";
    const string WarehouseSpecHeading = "## Serving at a warehouse";
    static readonly Regex WarehouseOutside = new(@"^[a-z][\w-]*[:#]", RegexOptions.Compiled);
    static readonly Regex UsesIm = new(@"^\s*(?:global\s+)?using\s+Im(?:\.[\w.]+)?\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

    static IEnumerable<Finding> WarehouseRule(RepoView repo)
    {
        var text = repo.ReadText(WarehouseRegister);
        if (text is null) { yield return new("DMP-016", WarehouseRegister, "missing: the list of information warehouses and whether each serves IM (constitution C19)"); yield break; }
        var rows = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        if (rows.Count == 0) yield return new("DMP-016", WarehouseRegister, "lists no warehouse");
        foreach (var r in rows)
        {
            if (r.Length != 3 || r[0].Trim().Length == 0 || r[2] is not ("yes" or "no"))
            { yield return new("DMP-016", WarehouseRegister, $"'{string.Join(' ', r)}' is not name<TAB>node<TAB>yes|no"); continue; }
            var node = r[1].Trim();
            if (node == "-" || WarehouseOutside.IsMatch(node)) continue;
            if (!repo.Exists(node)) { yield return new("DMP-016", WarehouseRegister, $"{r[0]}: the node '{node}' does not exist"); continue; }
            var usesIm = repo.Files(node + "/src").Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
                .FirstOrDefault(f => UsesIm.IsMatch(repo.ReadText(f) ?? ""));
            if (usesIm is not null && r[2] == "no")
                yield return new("DMP-016", usesIm, $"{r[0]} uses IM but the register says it does not serve IM; register it as 'yes' with its dual-signature test (constitution C19)");
            if (r[2] == "yes" && !repo.Exists(node + "/tests/Im/DualSignatureTests.cs"))
                yield return new("DMP-016", node + "/tests/Im/DualSignatureTests.cs", $"missing: {r[0]} serves IM, so this test must prove it refuses a request without both chains' signatures or with a grant the other chain did not sign");
        }
        if (!(repo.ReadText("vendor/dmp/04-im.md") ?? "").Contains(WarehouseSpecHeading, StringComparison.Ordinal))
            yield return new("DMP-016", "vendor/dmp/04-im.md", $"missing the section '{WarehouseSpecHeading.TrimStart('#', ' ')}' that says how a warehouse checks both chains itself");
    }
}
