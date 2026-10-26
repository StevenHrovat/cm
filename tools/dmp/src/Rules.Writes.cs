using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-012: a write first reads what it changes (constitution C15; chapter 10, "Guarded writes"). In a script under tools/ that is not -Kind ReadOnly:
// (a) every command that changes a system sits inside the -Write { } block of Invoke-GuardedWrite (tools/lib/Guard.psm1), or its line ends with
//     '# lint-ok: unguarded <why>'; today's unguarded calls are a per-script ratchet in tools/lib/unguarded-writes.txt whose numbers only go down;
// (b) every Invoke-GuardedWrite names -Read (with a command in it, so a cached variable cannot pass for a live read), -Expect, -DependentSteps,
//     -Write, and -Verify or -Done (the read back);
// (c) a script that connects to Microsoft Graph or Exchange Online asserts the tenant (Assert-Tenant) after it connects.
// Not checked, by design: that the read and the write name the same object and that -Expect is the right condition. Those stay review items.
public static partial class Rules
{
    const string UnguardedWrites = "tools/lib/unguarded-writes.txt";

    // The list of changing commands is the one DMP-011 uses (Rules.Scripts.cs: MutatingCmdlet, MutatingNative, AllowedLikeMutators), so both rules count the same calls.
    static readonly Regex WriteBlockHeader = new(@"-Write\s*$", RegexOptions.Compiled);

    static readonly Regex CommandInBlock = new(@"[A-Za-z]+-[A-Za-z][\w]*|&\s*\S|\.\w+\s*\(|\b[\w.]+\.exe\b", RegexOptions.Compiled);

    static IEnumerable<Finding> GuardedWrites(RepoView repo)
    {
        var ratchet = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in (repo.ReadText(UnguardedWrites) ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#'))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var n) || n < 1) { yield return new("DMP-012", UnguardedWrites, $"'{line}' is not 'path<TAB>count'"); continue; }
            ratchet[parts[0]] = n;
        }

        var scripts = AdminScripts(repo).ToList();
        foreach (var listed in ratchet.Keys.Where(k => !scripts.Contains(k)))
            yield return new("DMP-012", UnguardedWrites, $"'{listed}' is not a script under tools/ or shapes/ (delete the line)");

        foreach (var rel in scripts)
        {
            var raw = repo.ReadText(rel) ?? "";
            var stripped = PowerShellScan.StripComments(raw);
            if (Regex.IsMatch(stripped, @"Assert-Script\b[^\r\n]*-Kind\s+ReadOnly\b")) continue;
            var code = PowerShellScan.BlankStrings(stripped);              // a command name inside a message is not a command

            var calls = Regex.Matches(code, @"\bInvoke-GuardedWrite\b").Select(m => m.Index).ToList();
            foreach (var at in calls)
            {
                // (b) the parts of a guarded write
                var callText = CallExtent(code, at);
                var line = LineOf(code, at);
                foreach (var name in new[] { "Read", "Expect", "DependentSteps", "Write" })
                    if (!Regex.IsMatch(callText, $@"-{name}\b")) yield return new("DMP-012", rel, $"line {line}: Invoke-GuardedWrite without -{name}");
                if (!Regex.IsMatch(callText, @"-(Verify|Done)\b")) yield return new("DMP-012", rel, $"line {line}: Invoke-GuardedWrite without -Verify or -Done; every write is read back");
                if (Block(callText, "Read") is { } r && !CommandInBlock.IsMatch(callText[(r.From + 1)..(r.To - 1)]))
                    yield return new("DMP-012", rel, $"line {line}: the -Read block calls no command; the read must be live from the target, not a value read earlier");
            }

            // (a) unguarded writes, counted against the ratchet: a changing command counts unless a block around it is the -Write { } of a guarded write
            var rawLines = raw.Split('\n');
            var unguarded = new List<string>();
            var offset = 0;
            var lines = code.Split('\n');
            for (var n = 0; n < lines.Length; n++)
            {
                var text = lines[n];
                if (!(n < rawLines.Length && rawLines[n].Contains("lint-ok: unguarded", StringComparison.Ordinal)))
                {
                    foreach (var hit in MutatingCmdlet.Matches(text).Cast<Match>().Where(m => !AllowedLikeMutators.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
                                 .Concat(MutatingNative.Matches(text).Cast<Match>()))
                    {
                        var pos = offset + hit.Index;
                        if (!PowerShellScan.EnclosingHeaders(code, pos).Any(h => WriteBlockHeader.IsMatch(h.TrimEnd(' ', '\t', '\r', '\n', '`')))) unguarded.Add($"{n + 1}: {hit.Value.Trim()}");
                    }
                }
                offset += text.Length + 1;
            }
            var allowed = ratchet.GetValueOrDefault(rel);
            if (unguarded.Count > allowed)
                yield return new("DMP-012", rel, $"{unguarded.Count} writes outside Invoke-GuardedWrite (allowed {allowed}); wrap the new ones (first: line {unguarded[allowed]}) or end the line with '# lint-ok: unguarded <why>'");
            else if (unguarded.Count < allowed)
                yield return new("DMP-012", UnguardedWrites, $"'{rel}' has {unguarded.Count} unguarded writes now; lower its line to {unguarded.Count} (delete it at 0)");

            // (c) the tenant is expected state too
            var connect = Regex.Match(code, @"\bConnect-(MgGraph|ExchangeOnline)\b");
            if (connect.Success && !Regex.IsMatch(code[connect.Index..], @"\bAssert-Tenant\b"))
                yield return new("DMP-012", rel, $"line {LineOf(code, connect.Index)}: connects with Connect-{connect.Groups[1].Value} but never asserts the tenant (Assert-Tenant) after it");
        }
    }

    static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    /// <summary>The text of one command starting at <paramref name="from"/>: up to the first line break outside braces and parentheses that is not a backtick continuation.</summary>
    static string CallExtent(string code, int from)
    {
        var depth = 0;
        for (var i = from; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '{' or '(') depth++;
            else if (c is '}' or ')') { if (--depth < 0) return code[from..i]; }
            else if (c == '\n' && depth == 0)
            {
                var before = code[from..i].TrimEnd(' ', '\t', '\r');
                if (!before.EndsWith('`')) return code[from..i];
            }
        }
        return code[from..];
    }

    /// <summary>Start and end (exclusive, after the closing brace) of the script block passed to -<paramref name="name"/> inside a call text.</summary>
    static (int From, int To)? Block(string call, string name)
    {
        var m = Regex.Match(call, $@"-{name}\s*(?:`\r?\n\s*)?\{{");
        if (!m.Success) return null;
        var start = m.Index + m.Length - 1;
        var depth = 0;
        for (var i = start; i < call.Length; i++)
        {
            var c = call[i];
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return (start, i + 1);
        }
        return (start, call.Length);
    }
}
