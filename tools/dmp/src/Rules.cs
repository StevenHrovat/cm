using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cm.Dmp;

/// <summary>
/// The machine-checkable rules of DMP. A stable DMP section cites these ids in its "Enforced by:" line; rule DMP-007
/// refuses a stable section that cites an id that does not exist here. Adding a rule is a DMP change (chapter 00).
/// </summary>
public static partial class Rules
{
    public static IReadOnlyList<Rule> All { get; } =
    [
        new("DMP-001", "LICENSE is Apache 2.0", Licence),
        new("DMP-002", "CLAUDE.md opens with the defensive charter and carries the ask-the-nearest-tier-first, knowledge-goes-back and combine-every-path rules", Charter),
        new("DMP-003", "Package versions live only in Directory.Packages.props", CentralPackages),
        new("DMP-004", "Folders, projects and namespaces follow the tree in DMP/tree.txt", Tree),
        new("DMP-005", "C# on .NET 10: no other source language in the product folders, no other target framework", Language),
        new("DMP-006", "Roles, not values: no secret, address, mailbox or scrub-list value in the tree", RolesNotValues),
        new("DMP-007", "DMP document check: marks, owners, enforcing rules, version", DmpDocument),
        new("DMP-008", "Dependency direction: cm.io <- cm.os <- constellations, no constellation references another", DependencyDirection),
        new("DMP-009", "The authority grant in the constitution is word for word", AuthorityGrant),
        new("DMP-010", "Deterministic tests: no wall clock, machine name or stopwatch in tests", DeterministicTests),
        new("DMP-011", "Scripts an administrator runs start with the shared guard, say whether they change anything, have a dry run, and name no localized Windows group", ScriptGuard),
        new("DMP-012", "A write first reads what it changes: script writes sit in Invoke-GuardedWrite (live read, expected state, read back), the tenant is asserted", GuardedWrites),
        new("DMP-013", "Company data reaches general testing only with its proof: every feature that touches it is registered, and a feature at stage general has its handling document and GRB report", DataGate),
        new("DMP-014", "The levels of the tree: level 1 is approved by the owner, protocols are spoken only in their cm.io brick, every foundation part writes down its scope", Levels),
        new("DMP-015", "IM stays backwards compatible once locked: a locked version's corpus never changes and is replayed against today's verifier", ImLockRule),
        new("DMP-016", "A warehouse serves an IM request only when it carries both chains' signatures and every granted authority is co-signed by the other chain", WarehouseRule),
        new("DMP-017", "C∞, the stopgap, is the last section of the constitution and keeps its thesis", StopgapRule),
        new("DMP-018", "A section's laws are locked: the Law: block and its LAWS.bend agree word for word, the file's sha256 is in the section, and tools/verify proves them", LawsRule),
        new("DMP-020", "Every deployment obeys DMP: every workflow is registered check or deploy, a deploy workflow ships only after a gate job that runs tools/verify on the same commit, and the routes register keeps a route for every action with known gaps that only shrink", DeploymentGateRule),
        new("DMP-021", "The constitution is an axiom system: every theorem has a Derived-from line naming only sections above it, and the axiom count matches", DerivationRule),
        new("DMP-022", "Vendored copies are pinned: each vendor/ folder matches its PIN (source, commit, version, sha256)", VendorRule),
    ];

    public static IReadOnlySet<string> Ids { get; } = All.Select(r => r.Id).ToHashSet();

    public static IReadOnlyList<Finding> Run(RepoView repo, string ruleId) =>
        All.Single(r => r.Id == ruleId).Check(repo).ToList();

    // ---- DMP-001 ----------------------------------------------------------------------------------------------

    static IEnumerable<Finding> Licence(RepoView repo)
    {
        var text = repo.ReadText("LICENSE");
        if (text is null) { yield return new("DMP-001", "LICENSE", "missing"); yield break; }
        if (!text.Contains("Apache License") || !text.Contains("Version 2.0"))
            yield return new("DMP-001", "LICENSE", "is not the Apache License, Version 2.0");
    }

    // ---- DMP-002 ----------------------------------------------------------------------------------------------

    static IEnumerable<Finding> Charter(RepoView repo)
    {
        var text = repo.ReadText("CLAUDE.md");
        if (text is null) { yield return new("DMP-002", "CLAUDE.md", "missing"); yield break; }
        var firstSection = text.Split('\n').FirstOrDefault(l => l.StartsWith("## ", StringComparison.Ordinal));
        if (firstSection is null || !firstSection.StartsWith("## Defensive security charter", StringComparison.Ordinal))
            yield return new("DMP-002", "CLAUDE.md", "the first section must be '## Defensive security charter'");
        foreach (var needle in new[] { "Ask the nearest tier first", "Knowledge goes back into Tier 1", "Combine every path", "Look it up, don't guess" })
            if (!text.Contains(needle)) yield return new("DMP-002", "CLAUDE.md", $"the ask-first, write-back or combine-every-path rule is missing ('{needle}')");
        // C22: proof runs both ways; the CLAUDE.md bullet keeps every session aware of it.
        if (!text.Contains("Prove forward and back"))
            yield return new("DMP-002", "CLAUDE.md", "the prove-forward-and-back rule (C22) is missing ('Prove forward and back')");
        // C25: every deployment obeys DMP (gate on every deployment, no lockout).
        if (!text.Contains("Every deployment obeys DMP"))
            yield return new("DMP-002", "CLAUDE.md", "the deployment rule (C25) is missing ('Every deployment obeys DMP')");
        // C∞: the thesis and stopgap of DMP; the CLAUDE.md bullet keeps every session aware of it.
        if (!text.Contains("What is not fully trusted is ejected"))
            yield return new("DMP-002", "CLAUDE.md", "the stopgap (C∞) is missing ('What is not fully trusted is ejected')");
    }

    // ---- DMP-003 ----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"<PackageReference\b[^>]*\bVersion\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex VersionOnReference();

    static IEnumerable<Finding> CentralPackages(RepoView repo)
    {
        var central = repo.ReadText("Directory.Packages.props");
        if (central is null) yield return new("DMP-003", "Directory.Packages.props", "missing");
        else if (!Regex.IsMatch(central, @"<ManagePackageVersionsCentrally>\s*true\s*</ManagePackageVersionsCentrally>"))
            yield return new("DMP-003", "Directory.Packages.props", "ManagePackageVersionsCentrally is not true");

        foreach (var file in repo.Files().Where(f => IsMsBuildFile(f) && !f.StartsWith("vendor/", StringComparison.Ordinal)))
        {
            var text = repo.ReadText(file) ?? "";
            if (VersionOnReference().IsMatch(text)) yield return new("DMP-003", file, "a PackageReference carries a Version; versions belong in Directory.Packages.props");
            if (file != "Directory.Packages.props" && text.Contains("<PackageVersion ", StringComparison.Ordinal))
                yield return new("DMP-003", file, "a PackageVersion outside Directory.Packages.props");
        }
    }

    static bool IsMsBuildFile(string path) =>
        path.EndsWith(".csproj", StringComparison.Ordinal) || path.EndsWith(".props", StringComparison.Ordinal) || path.EndsWith(".targets", StringComparison.Ordinal);

    // ---- DMP-004 ----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

    static IEnumerable<Finding> Tree(RepoView repo)
    {
        var names = repo.TreeNames();
        if (names.Count == 0) { yield return new("DMP-004", "DMP/tree.txt", "missing or empty"); yield break; }

        foreach (var folder in repo.RootDirectories().Where(RepoView.LooksLikeProduct))
            if (!names.Contains(folder, StringComparer.Ordinal))
                yield return new("DMP-004", folder, "a top-level folder named like a tree node that DMP/tree.txt does not list; adding a node is a DMP change");

        foreach (var name in names)
        {
            if (!repo.Exists($"{name}/src/{name}.csproj")) yield return new("DMP-004", name, $"missing {name}/src/{name}.csproj");
            if (!repo.Exists($"{name}/tests/{name}.Tests.csproj")) yield return new("DMP-004", name, $"missing {name}/tests/{name}.Tests.csproj (empty-but-tested)");
            foreach (var file in repo.Files(name).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
                foreach (Match m in NamespaceDeclaration().Matches(repo.ReadText(file) ?? ""))
                {
                    var ns = m.Groups[1].Value;
                    if (ns != name && !ns.StartsWith(name + ".", StringComparison.Ordinal))
                        yield return new("DMP-004", file, $"namespace '{ns}' is outside '{name}'; the namespace is the place in the tree");
                }
        }
    }

    // ---- DMP-005 ----------------------------------------------------------------------------------------------

    static readonly string[] ForeignSourceExtensions =
        [".py", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".java", ".kt", ".go", ".rs", ".rb", ".php", ".c", ".cc", ".cpp", ".h", ".swift", ".lua", ".pl"];

    [GeneratedRegex(@"<TargetFrameworks?>([^<]+)</TargetFrameworks?>")]
    private static partial Regex TargetFramework();

    static IEnumerable<Finding> Language(RepoView repo)
    {
        foreach (var name in repo.TreeNames())
            foreach (var file in repo.Files(name))
                if (ForeignSourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return new("DMP-005", file, "source in a language other than C#; a product folder holds C# only unless a decision names an exception");

        var build = repo.ReadText("Directory.Build.props");
        if (build is null || !build.Contains("<TargetFramework>net10.0</TargetFramework>"))
            yield return new("DMP-005", "Directory.Build.props", "must set TargetFramework to net10.0");
        foreach (var file in repo.Files().Where(f => IsMsBuildFile(f) && !f.StartsWith("vendor/", StringComparison.Ordinal)))
            foreach (Match m in TargetFramework().Matches(repo.ReadText(file) ?? ""))
                foreach (var tfm in m.Groups[1].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!tfm.StartsWith("net10.0", StringComparison.Ordinal))
                        yield return new("DMP-005", file, $"target framework '{tfm}' is not net10.0");
    }

    // ---- DMP-006 ----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9_.-]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-ant-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16}")]
    private static partial Regex SecretShape();

    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\.?\d)")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\b")]
    private static partial Regex Mailbox();

    [GeneratedRegex(@"\\\\[A-Za-z0-9][A-Za-z0-9_.-]*\\[A-Za-z0-9$_-]+")]
    private static partial Regex UncPath();

    static readonly string[] PlaceholderMailDomains = ["example.com", "example.org", "example.net", "users.noreply.github.com"];

    static bool IsPlaceholderAddress(string ip) =>
        ip == "0.0.0.0" || ip.StartsWith("127.", StringComparison.Ordinal) || ip.StartsWith("192.0.2.", StringComparison.Ordinal)
        || ip.StartsWith("198.51.100.", StringComparison.Ordinal) || ip.StartsWith("203.0.113.", StringComparison.Ordinal) || ip.StartsWith("255.", StringComparison.Ordinal);

    static IEnumerable<Finding> RolesNotValues(RepoView repo)
    {
        foreach (var file in repo.Files())
        {
            var text = repo.ReadTextIfText(file);
            if (text is null) continue;
            if (SecretShape().IsMatch(text)) yield return new("DMP-006", file, "looks like a secret or a key");
            foreach (var line in text.Split('\n'))
            {
                if (line.Contains("version", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Match m in Ipv4().Matches(line))
                    if (!IsPlaceholderAddress(m.Value)) { yield return new("DMP-006", file, "an IPv4 address; write the role and take the value from the settings store"); break; }
            }
            foreach (Match m in Mailbox().Matches(text))
            {
                var domain = m.Value[(m.Value.IndexOf('@') + 1)..];
                if (!PlaceholderMailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                { yield return new("DMP-006", file, "a mailbox address; write the role"); break; }
            }
            if (UncPath().IsMatch(text)) yield return new("DMP-006", file, "a UNC share path; write the role");
            foreach (var entry in repo.ScrubList)
                if (text.Contains(entry, StringComparison.OrdinalIgnoreCase))
                    yield return new("DMP-006", file, "contains a value from the scrub list (the value is not printed)");
        }
    }

    // ---- DMP-007 ----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"^Status: (draft|stable since DMP (\d+)\.(\d+)\.(\d+))\s*$", RegexOptions.Multiline)]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"^Enforced by: (.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex EnforcedLine();

    [GeneratedRegex(@"^Owner: .+$", RegexOptions.Multiline)]
    private static partial Regex OwnerLine();

    [GeneratedRegex(@"^\d\d-.+\.md$")]
    private static partial Regex ChapterFileName();

    /// <summary>Sections that are stable but enforced by prose only; the runner prints them so nobody mistakes prose for a gate.</summary>
    public static IReadOnlyList<string> UnenforcedStableSections(RepoView repo)
    {
        var result = new List<string>();
        foreach (var (file, title, body) in Sections(repo))
        {
            var status = StatusLine().Match(body);
            var enforced = EnforcedLine().Match(body);
            if (status.Success && status.Groups[1].Value.StartsWith("stable", StringComparison.Ordinal) && enforced.Success && enforced.Groups[1].Value == "none yet")
                result.Add($"{file}: {title}");
        }
        return result;
    }

    static IEnumerable<(string File, string Title, string Body)> Sections(RepoView repo)
    {
        foreach (var file in repo.Files("vendor/dmp").Where(f => f.Count(c => c == '/') == 2 && ChapterFileName().IsMatch(Path.GetFileName(f))).Order())
        {
            string? title = null;
            var body = new StringBuilder();
            foreach (var line in (repo.ReadText(file) ?? "").Split('\n'))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (title is not null) yield return (file, title, body.ToString());
                    title = line[3..].Trim();
                    body.Clear();
                }
                else if (title is not null) body.AppendLine(line.TrimEnd('\r'));
            }
            if (title is not null) yield return (file, title, body.ToString());
        }
    }

    static IEnumerable<Finding> DmpDocument(RepoView repo)
    {
        var versionText = repo.ReadText("vendor/dmp/VERSION")?.Trim();
        if (versionText is null || !Version.TryParse(versionText, out var current) || versionText.Count(c => c == '.') != 2)
        {
            yield return new("DMP-007", "vendor/dmp/VERSION", "missing or not MAJOR.MINOR.PATCH");
            current = new Version(0, 0, 0);
        }
        foreach (var required in new[] { "vendor/dmp/00-preamble.md", "vendor/dmp/01-constitution.md" })
            if (!repo.Exists(required)) yield return new("DMP-007", required, "missing");

        foreach (var (file, title, body) in Sections(repo))
        {
            var status = StatusLine().Match(body);
            if (!status.Success) { yield return new("DMP-007", file, $"section '{title}' has no 'Status: draft' or 'Status: stable since DMP x.y.z' line"); continue; }
            var stable = status.Groups[1].Value.StartsWith("stable", StringComparison.Ordinal);
            if (!stable)
            {
                if (file.EndsWith("01-constitution.md", StringComparison.Ordinal)) yield return new("DMP-007", file, $"section '{title}' of the constitution must be stable");
                continue;
            }
            var since = new Version(int.Parse(status.Groups[2].Value), int.Parse(status.Groups[3].Value), int.Parse(status.Groups[4].Value));
            if (since > current) yield return new("DMP-007", file, $"section '{title}' is stable since a version newer than vendor/dmp/VERSION");
            if (!OwnerLine().IsMatch(body)) yield return new("DMP-007", file, $"stable section '{title}' has no 'Owner:' line");
            var enforced = EnforcedLine().Match(body);
            if (!enforced.Success) { yield return new("DMP-007", file, $"stable section '{title}' has no 'Enforced by:' line (use 'none yet' when no check exists)"); continue; }
            if (enforced.Groups[1].Value == "none yet") continue;
            foreach (var id in enforced.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
                if (!Ids.Contains(id)) yield return new("DMP-007", file, $"stable section '{title}' cites unknown rule '{id}'");
        }
    }

    // ---- DMP-008 ----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"<ProjectReference\s+Include\s*=\s*""([^""]+)""")]
    private static partial Regex ProjectReference();

    static IEnumerable<Finding> DependencyDirection(RepoView repo)
    {
        foreach (var name in repo.TreeNames())
        {
            var allowed = name switch
            {
                "cm.io" => Array.Empty<string>(),
                "cm.os" => ["cm.io"],
                _ => ["cm.io", "cm.os"],
            };
            foreach (var file in repo.Files(name).Where(f => f.EndsWith(".csproj", StringComparison.Ordinal)))
                foreach (Match m in ProjectReference().Matches(repo.ReadText(file) ?? ""))
                {
                    var target = m.Groups[1].Value.Replace('\\', '/');
                    var targetNode = target.Split('/').FirstOrDefault(p => repo.TreeNames().Contains(p, StringComparer.Ordinal));
                    if (targetNode is null || targetNode == name) continue;
                    if (!allowed.Contains(targetNode))
                        yield return new("DMP-008", file, $"{name} references {targetNode}; constellations meet through Milky Way (IM), not through project references");
                }
        }
    }

    // ---- DMP-009 ----------------------------------------------------------------------------------------------

    /// <summary>SHA-256 of the normalised authority grant (whitespace collapsed, typographic quotes straightened). Changing the grant is the owner's decision, so it is a change of this constant, which `dmp` then shows in review.</summary>
    public const string AuthorityGrantSha256 = "6eff36a82731f7ed7bdde5a8de6382f381680e92e8ceec282db31b5ad823de7a";

    public static string NormaliseGrant(string text)
    {
        var straight = text.Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u201C', '"').Replace('\u201D', '"');
        var unquoted = Regex.Replace(straight, @"^\s*>\s?", "", RegexOptions.Multiline);
        return string.Join(' ', unquoted.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string GrantHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormaliseGrant(text))));

    static IEnumerable<Finding> AuthorityGrant(RepoView repo)
    {
        const string begin = "<!-- authority-grant:begin -->", end = "<!-- authority-grant:end -->";
        var text = repo.ReadText("vendor/dmp/01-constitution.md");
        if (text is null) { yield return new("DMP-009", "vendor/dmp/01-constitution.md", "missing"); yield break; }
        var from = text.IndexOf(begin, StringComparison.Ordinal);
        var to = text.IndexOf(end, StringComparison.Ordinal);
        if (from < 0 || to < from) { yield return new("DMP-009", "vendor/dmp/01-constitution.md", "the authority grant block is missing"); yield break; }
        var grant = text[(from + begin.Length)..to];
        if (GrantHash(grant) != AuthorityGrantSha256)
            yield return new("DMP-009", "vendor/dmp/01-constitution.md", "the authority grant differs from the recorded wording; only the owner changes it");
    }

    // ---- DMP-010 ----------------------------------------------------------------------------------------------

    [GeneratedRegex(@"\b(?:DateTime|DateTimeOffset)\.(?:Now|UtcNow)\b|Environment\.MachineName|\bStopwatch\b")]
    private static partial Regex WallClock();

    static IEnumerable<Finding> DeterministicTests(RepoView repo)
    {
        foreach (var file in repo.Files().Where(f => f.EndsWith(".cs", StringComparison.Ordinal) && f.Contains("/tests/", StringComparison.Ordinal)))
            if (WallClock().IsMatch(repo.ReadText(file) ?? ""))
                yield return new("DMP-010", file, "uses the wall clock, the machine name or a stopwatch; inject TimeProvider or the machine identity (chapter 10)");
    }
}
