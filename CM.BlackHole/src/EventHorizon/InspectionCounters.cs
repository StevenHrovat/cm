using System.Collections.Concurrent;
using System.Globalization;
using cm.os.Inspection;

namespace CM.BlackHole.EventHorizon;

/// <summary>
/// Counts, never content (CMB, constitution C3, DMP chapter 06). Event Horizon records, per window, how many connections
/// got each depth and why, keyed by <c>(category, depth, reason)</c>. A category is a low-cardinality tag the resolver
/// assigns (for example <c>banking-finance</c>), never a host, a subject, an address or any payload. Three fixed bucket keys
/// stand in for what is not a tag, so no destination name can ever appear in a key:
/// <see cref="UncategorisedKey"/> for no category, <see cref="InvalidCategoryKey"/> for a string that is not a category
/// (a resolver or feed bug, visible as its own count; GRB-0002 finding C1), and <see cref="OverflowKey"/> once
/// <see cref="MaxKeys"/> distinct keys exist (a resource limit, so a feed that emits unique tags cannot grow the process;
/// GRB-0002 finding C2).
/// </summary>
public sealed class InspectionCounters
{
    public const string UncategorisedKey = "(uncategorised)";
    public const string InvalidCategoryKey = "(invalid-category)";
    public const string OverflowKey = "(overflow)";

    /// <summary>The most distinct keys the counters hold; the rest is counted under <see cref="OverflowKey"/>.</summary>
    public const int MaxKeys = 1024;

    readonly ConcurrentDictionary<string, long> counts = new(StringComparer.Ordinal);

    /// <summary>Record one decided connection. Takes the verdict and the category only; the host is deliberately not passed.</summary>
    public void Record(string? category, InspectionVerdict verdict)
    {
        var key = Key(category, verdict.Depth, verdict.Reason);
        if (!counts.ContainsKey(key) && counts.Count >= MaxKeys - 1)
            key = OverflowKey;
        counts.AddOrUpdate(key, 1, static (_, n) => n + 1);
    }

    /// <summary>A snapshot of the counters, ordered, for a health report or the Monitoring page. Keys only, counts only.</summary>
    public IReadOnlyDictionary<string, long> Snapshot() =>
        counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    static string Key(string? category, InspectionDepth depth, InspectionReason reason)
    {
        var cat = string.IsNullOrWhiteSpace(category) ? UncategorisedKey
            : CategoryTag.TryNormalise(category) ?? InvalidCategoryKey;
        return string.Create(CultureInfo.InvariantCulture, $"{cat}|{depth}|{reason}");
    }
}
