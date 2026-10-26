using System.Text.RegularExpressions;

namespace CM.BlackHole.EventHorizon;

/// <summary>How serious a finding is. Drives whether it reaches the Status board and the morning report.</summary>
public enum FindingSeverity
{
    Info,
    Low,
    Medium,
    High,
}

/// <summary>
/// A sealed finding (CM.BlackHole/docs/event-horizon-inspection.md section 4): a rule match that leaves Event Horizon
/// carrying a <b>category, a rule id, a count and a severity</b> — never a host, a device subject, an address or content.
/// "Sealed" because what would identify a person or a payload is removed at the source, not masked afterwards. A finding
/// with a specific target stays inside Event Horizon as a count; only the sealed record crosses any border.
///
/// The two strings have a grammar, enforced in the constructor so free text cannot ride out inside the record (GRB-0002 finding C4):
/// the rule id is <c>eh.</c> followed by one or more lower-case words (<c>eh.malware-domain</c>), the category is a valid
/// <see cref="CategoryTag"/> or the counters' <see cref="InspectionCounters.UncategorisedKey"/>.
/// </summary>
public sealed partial record SealedFinding
{
    [GeneratedRegex(@"^eh(\.[a-z0-9]+(-[a-z0-9]+)*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex RuleIdGrammar();

    /// <param name="ruleId">The rule that matched, for example <c>eh.malware-domain</c>. From a fixed vocabulary, never free text.</param>
    /// <param name="category">The destination category the match fell under, or <see cref="InspectionCounters.UncategorisedKey"/>.</param>
    /// <param name="count">How many connections matched in the window.</param>
    /// <param name="severity">How serious the match is.</param>
    public SealedFinding(string ruleId, string category, long count, FindingSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (ruleId.Length > 96 || !RuleIdGrammar().IsMatch(ruleId))
            throw new ArgumentException("a rule id is 'eh.' followed by lower-case words, never free text", nameof(ruleId));
        var tag = category == InspectionCounters.UncategorisedKey ? category : CategoryTag.TryNormalise(category);
        if (tag is null)
            throw new ArgumentException("a finding category is a category tag or the uncategorised bucket, never a host or free text", nameof(category));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "a finding count is never negative");
        RuleId = ruleId;
        Category = tag;
        Count = count;
        Severity = severity;
    }

    public string RuleId { get; }
    public string Category { get; }
    public long Count { get; }
    public FindingSeverity Severity { get; }
}
