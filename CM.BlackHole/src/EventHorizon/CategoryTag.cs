using System.Text.RegularExpressions;

namespace CM.BlackHole.EventHorizon;

/// <summary>
/// The grammar of a destination category ("tag"). A category is a low-cardinality label the CMOS resolver or a category feed
/// assigns to a destination, for example <c>banking-finance</c>; it is the one string from outside that reaches the counters and
/// the sealed findings (CMB, constitution C3), so its shape is fixed here and enforced wherever it enters:
/// <list type="bullet">
///   <item><description>lower-case ASCII letters and digits in hyphen-separated words, 1 to 64 characters, no dots, no spaces,
///   no slashes: a host, an address, an e-mail address or free text can never pass as a category;</description></item>
///   <item><description>the telemetry bucket keys (<c>(uncategorised)</c>, <c>(invalid-category)</c>, <c>(overflow)</c>) are reserved
///   words of the counters and are not categories.</description></item>
/// </list>
/// Normalisation (trim, lower-case invariant) happens once, here, so the policy's comparison never depends on the comparer a
/// caller gave its sets (GRB-0002 finding A4). Added in the GRB-0002 hardening (2026-10-25).
/// </summary>
public static partial class CategoryTag
{
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Grammar();

    /// <summary>The normalised tag, or null when <paramref name="value"/> is not a category (empty, whitespace, bucket key, wrong shape).</summary>
    public static string? TryNormalise(string? value)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0 || v.Length > MaxLength) return null;
        if (!IsAscii(v)) return null;
        v = v.ToLowerInvariant();
        return Grammar().IsMatch(v) ? v : null;
    }

    public static bool IsValid(string? value) => TryNormalise(value) is not null;

    /// <summary>The set of valid, normalised tags in <paramref name="values"/>; invalid entries are dropped (they could never match).</summary>
    public static HashSet<string> NormaliseSet(IEnumerable<string>? values)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (values is null) return set;
        foreach (var v in values)
            if (TryNormalise(v) is { } tag) set.Add(tag);
        return set;
    }

    internal static bool IsAscii(string s)
    {
        foreach (var c in s) if (c > 0x7E || c < 0x21) return false;
        return true;
    }
}
