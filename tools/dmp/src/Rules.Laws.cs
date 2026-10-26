using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-018: a section's laws are locked (Gravity, CM.BlackHole/docs/gravity.md "How a section gets laws").
// A section may carry a `Law:` block (items "- C2.L1 <sentence>", wrapped lines indented) and a line
// "Laws file: <path>/LAWS.bend sha256 <hex>". The rule checks:
// (a) the two come together: a Law: block without a Laws file line, or the reverse, is refused;
// (b) the laws file exists and its sha256 (line ends normalised to \n) matches, so LAWS.bend changes only with the section;
// (c) every law of the block is quoted word for word in the laws file as a comment paragraph "# C2.L1 <sentence>",
//     and every law the file quotes is in the block, so the sentences of record and the formal laws cannot drift apart;
// (d) the folder holds what proves the laws (model.bend, PROOF.bend, mutants.json), the section is enforced by DMP-018,
//     and tools/verify still runs the gravity group (tools/gravity), which proves the laws where Bend is installed.
// Sentences are compared with whitespace collapsed and backticks dropped.
public static partial class Rules
{
    const string LawsRuleId = "DMP-018";
    static readonly Regex LawsFileLine = new(@"^Laws file: (\S+) sha256 ([0-9a-f]{64})\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    static readonly Regex LawItem = new(@"^- (C\d+\.L\d+[a-z]?) (.+)$", RegexOptions.Compiled);
    static readonly Regex LawQuote = new(@"^# (C\d+\.L\d+[a-z]?) (.+)$", RegexOptions.Compiled);

    static IEnumerable<Finding> LawsRule(RepoView repo)
    {
        foreach (var (file, title, body) in Sections(repo))
        {
            var block = LawBlock(body);
            var lock_ = LawsFileLine.Match(body);
            if (block is null && !lock_.Success) continue;
            if (block is null) { yield return new(LawsRuleId, file, $"section '{title}' locks a laws file but has no 'Law:' block"); continue; }
            if (!lock_.Success) { yield return new(LawsRuleId, file, $"section '{title}' has a 'Law:' block but no 'Laws file: <path> sha256 <hex>' line"); continue; }
            if (block.Count == 0) yield return new(LawsRuleId, file, $"the 'Law:' block of '{title}' lists no law");

            var lawsPath = lock_.Groups[1].Value;
            var text = repo.ReadText(lawsPath);
            if (text is null) { yield return new(LawsRuleId, lawsPath, $"missing: the laws file that '{title}' locks"); continue; }
            text = text.Replace("\r\n", "\n");
            var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (actual != lock_.Groups[2].Value)
                yield return new(LawsRuleId, lawsPath, $"changed without the constitution: its sha256 is {actual}, '{title}' locks {lock_.Groups[2].Value}; a law changes only with the owner's approval and a cold read");

            var quoted = LawQuotes(text);
            foreach (var (id, sentence) in block)
            {
                if (!quoted.TryGetValue(id, out var q)) yield return new(LawsRuleId, lawsPath, $"law {id} of '{title}' is not quoted ('# {id} ...')");
                else if (q != sentence) yield return new(LawsRuleId, lawsPath, $"law {id} is quoted differently from '{title}': the file says '{q}', the section says '{sentence}'");
            }
            foreach (var id in quoted.Keys.Where(id => !block.ContainsKey(id)))
                yield return new(LawsRuleId, lawsPath, $"quotes law {id}, which the 'Law:' block of '{title}' does not have");

            var folder = Path.GetDirectoryName(lawsPath)!.Replace('\\', '/');
            foreach (var needed in new[] { "model.bend", "PROOF.bend", "mutants.json" })
                if (!repo.Exists($"{folder}/{needed}")) yield return new(LawsRuleId, $"{folder}/{needed}", $"missing: the laws of '{title}' are proved and tested from this folder");
            var enforced = EnforcedLine().Match(body);
            if (!enforced.Success || !enforced.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries).Contains(LawsRuleId))
                yield return new(LawsRuleId, file, $"section '{title}' has laws, so its 'Enforced by:' line must cite {LawsRuleId}");
        }
        foreach (var verify in new[] { "tools/verify", "tools/verify.ps1" })
            if (repo.Exists(verify) && !(repo.ReadText(verify) ?? "").Contains("tools/gravity", StringComparison.Ordinal))
                yield return new(LawsRuleId, verify, "no longer runs the gravity group (tools/gravity) that proves the laws");
    }

    /// <summary>The items of a section's `Law:` block by id, sentences normalised; null when the section has no block.</summary>
    static Dictionary<string, string>? LawBlock(string body)
    {
        var lines = body.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(l => l.Trim() == "Law:");
        if (start < 0) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? id = null;
        var sentence = new StringBuilder();
        for (var i = start + 1; i < lines.Count && lines[i].Trim().Length > 0; i++)
        {
            var m = LawItem.Match(lines[i]);
            if (m.Success)
            {
                if (id is not null) result[id] = NormaliseLaw(sentence.ToString());
                id = m.Groups[1].Value;
                sentence.Clear().Append(m.Groups[2].Value);
            }
            else if (id is not null && lines[i].StartsWith(' ')) sentence.Append(' ').Append(lines[i].Trim());
            else break;
        }
        if (id is not null) result[id] = NormaliseLaw(sentence.ToString());
        return result;
    }

    /// <summary>The comment paragraphs of a laws file that start "# Cn.Lm", by id; a paragraph ends at a bare "#" or a non-comment line.</summary>
    static Dictionary<string, string> LawQuotes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = LawQuote.Match(lines[i]);
            if (!m.Success) continue;
            var sentence = new StringBuilder(m.Groups[2].Value);
            for (var j = i + 1; j < lines.Length && lines[j].StartsWith("# ", StringComparison.Ordinal) && !LawQuote.IsMatch(lines[j]); j++)
                sentence.Append(' ').Append(lines[j][2..]);
            result[m.Groups[1].Value] = NormaliseLaw(sentence.ToString());
        }
        return result;
    }

    static string NormaliseLaw(string sentence) => Regex.Replace(sentence.Replace("`", ""), @"\s+", " ").Trim();
}
