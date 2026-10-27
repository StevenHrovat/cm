using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-021: the constitution is an axiom system (DMP 0.9.0). Its sections come in the order of proof:
// "The words", the axioms ("Axiom n ·"), the ground ("G0 ·"), then theorems ("Tn ·", "T-P ·", "Cn ·") and C∞ last (DMP-017).
// Every theorem carries one "Derived from:" line that names only labels of sections ABOVE it (the words, an axiom, G0, a theorem),
// so no theorem rests on itself or on a later one. A section that is neither the words, an axiom nor G0 and has no such line is refused:
// it would be an axiom the chapter does not list at the top. The axioms are counted in the chapter's intro ("Counts.") and must match.
public static partial class Rules
{
    const string DerivationRuleId = "DMP-021";

    // The "Derived from:" paragraph: from the line that starts with it to the next blank line.
    [GeneratedRegex(@"^Derived from: (.+(?:\r?\n(?!\s*\r?\n).+)*)", RegexOptions.Multiline)]
    private static partial Regex DerivedFromLine();

    // A citation: "Axiom 1", "G0", "T8", "T8a", "T16b", "T-P", "C17", or "the words".
    [GeneratedRegex(@"\bAxiom \d+\b|\bG0\b|\bT-P\b|\bT\d+[a-d]?\b|\bC\d+\b|\bthe words\b")]
    private static partial Regex Citation();

    // The label a heading defines: "Axiom 2 · Evidence" -> "Axiom 2"; "T16b · Lockstep" -> "T16b"; "The words" -> "the words".
    static string? LabelOf(string title)
    {
        if (title == "The words") return "the words";
        var dot = title.IndexOf(" ·", StringComparison.Ordinal);
        return dot < 0 ? null : title[..dot];
    }

    static IEnumerable<Finding> DerivationRule(RepoView repo)
    {
        var text = repo.ReadText(ConstitutionFile);
        if (text is null) { yield return new(DerivationRuleId, ConstitutionFile, "missing"); yield break; }

        var sections = Sections(repo).Where(s => s.File == ConstitutionFile).ToList();
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var axioms = 0;
        foreach (var (_, title, body) in sections)
        {
            var label = LabelOf(title);
            if (label is null) { yield return new(DerivationRuleId, ConstitutionFile, $"section '{title}' has no label ('<label> · <title>')"); continue; }
            var isAxiom = label.StartsWith("Axiom ", StringComparison.Ordinal);
            if (isAxiom) axioms++;
            var lines = DerivedFromLine().Matches(body);
            if (label is "the words" || isAxiom || label == "G0")
            {
                if (lines.Count > 0) yield return new(DerivationRuleId, ConstitutionFile, $"'{title}' is not derived; it must not carry a 'Derived from:' line");
            }
            else if (label == "C∞") { /* the meta-rule; DMP-017 keeps it last */ }
            else if (lines.Count != 1)
                yield return new(DerivationRuleId, ConstitutionFile, $"theorem '{title}' needs exactly one 'Derived from:' line naming only sections above it; without one it is an axiom the chapter does not list");
            else
            {
                var cited = Citation().Matches(lines[0].Groups[1].Value).Select(m => m.Value).Distinct().ToList();
                if (cited.Count == 0) yield return new(DerivationRuleId, ConstitutionFile, $"the 'Derived from:' line of '{title}' cites nothing it rests on");
                foreach (var c in cited.Where(c => !defined.Contains(c)))
                    yield return new(DerivationRuleId, ConstitutionFile, c == label
                        ? $"'{title}' cites itself"
                        : $"'{title}' cites '{c}', which is not a section above it (a theorem rests only on what precedes it; no loops, no forward references)");
            }
            if (!defined.Add(label)) yield return new(DerivationRuleId, ConstitutionFile, $"the label '{label}' is used by two sections");
        }

        var counts = Regex.Match(text, @"\*\*Counts\.\*\* (\d+) bounds, (\d+) direction");
        if (!counts.Success) yield return new(DerivationRuleId, ConstitutionFile, "the intro must state the axiom count ('**Counts.** <n> bounds, <n> direction')");
        else if (int.Parse(counts.Groups[1].Value) + int.Parse(counts.Groups[2].Value) != axioms)
            yield return new(DerivationRuleId, ConstitutionFile, $"the intro counts {counts.Groups[1].Value} bounds and {counts.Groups[2].Value} direction, but the chapter has {axioms} axiom sections");
    }
}
