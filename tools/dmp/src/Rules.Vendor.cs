using System.Security.Cryptography;
using System.Text;

namespace Cm.Dmp;

// DMP-022: every vendored copy is byte for byte what its PIN says.
// Each folder under vendor/ carries a PIN file: tab-separated lines "source", "commit", "version" and "sha256". The sha256 is over every file of the
// folder except PIN, in ordinal path order, each as "<relative path>\n<bytes>" (the recipe of DMP/im-lock.txt). A vendored copy that no longer matches
// its PIN fails; so does a vendor folder without a PIN.
// Only the three repositories of the suite are vendored in the root seed (DMP, Gravity, IM). Vendoring and verifying every other dependency is mint prep (docs/dependencies.md).
public static partial class Rules
{
    const string VendorRoot = "vendor";

    public static string VendorHash(RepoView repo, string folder)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in repo.Files(folder).Where(f => f != folder + "/PIN").Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(file[(folder.Length + 1)..] + "\n"));
            sha.AppendData(repo.ReadBytes(file));
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    static IEnumerable<Finding> VendorRule(RepoView repo)
    {
        var folders = repo.Files(VendorRoot).Select(f => f.Split('/')).Where(p => p.Length > 2).Select(p => p[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        foreach (var name in folders)
        {
            var folder = $"{VendorRoot}/{name}";
            var pin = repo.ReadText(folder + "/PIN");
            if (pin is null) { yield return new("DMP-022", folder + "/PIN", "missing: every vendored dependency carries a PIN (source, commit, version, sha256)"); continue; }
            var fields = pin.Split('\n').Select(l => l.TrimEnd('\r').Split('\t')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1].Trim());
            foreach (var key in new[] { "source", "commit", "version", "sha256" })
                if (!fields.TryGetValue(key, out var v) || v.Length == 0) yield return new("DMP-022", folder + "/PIN", $"has no '{key}' line (key<TAB>value)");
            if (fields.TryGetValue("sha256", out var recorded) && VendorHash(repo, folder) != recorded.ToLowerInvariant())
                yield return new("DMP-022", folder, "no longer matches its PIN; a vendored copy changes only by re-pinning it from its source");
        }
    }
}
