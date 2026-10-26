namespace Cm.Dmp;

// DMP-017: C∞ is the thesis and permanent stopgap of DMP (constitution, reserved number C23). It always stands last, so every new
// section is inserted above it, and its section keeps the owner's thesis sentence.
public static partial class Rules
{
    const string ConstitutionFile = "vendor/dmp/01-constitution.md";
    const string StopgapHeading = "## C∞ ·";
    const string StopgapThesis = "Everything that is not fully trusted is automatically ejected";

    static IEnumerable<Finding> StopgapRule(RepoView repo)
    {
        var text = repo.ReadText(ConstitutionFile);
        if (text is null) { yield return new("DMP-017", ConstitutionFile, "missing"); yield break; }
        var headings = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToList();
        var stopgap = headings.FindIndex(h => h.StartsWith(StopgapHeading, StringComparison.Ordinal));
        if (stopgap < 0) { yield return new("DMP-017", ConstitutionFile, "the stopgap section 'C∞' is missing"); yield break; }
        if (stopgap != headings.Count - 1)
            yield return new("DMP-017", ConstitutionFile, $"C∞ must be the last section; '{headings[^1]}' stands after it (insert new sections above C∞)");
        var body = text[text.IndexOf(StopgapHeading, StringComparison.Ordinal)..];
        if (!body.Contains(StopgapThesis, StringComparison.Ordinal))
            yield return new("DMP-017", ConstitutionFile, $"C∞ must keep the owner's thesis '{StopgapThesis}'");
    }
}
