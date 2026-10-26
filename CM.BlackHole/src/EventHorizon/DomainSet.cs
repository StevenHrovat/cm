namespace CM.BlackHole.EventHorizon;

/// <summary>
/// A set of destination patterns, matched against a connection's host. Two pattern shapes, both as they appear in the
/// Microsoft 365 endpoints web service and in a CMOS setting:
/// <list type="bullet">
///   <item><description><c>example.com</c> — an exact host (and, for a registrable domain, its subdomains too).</description></item>
///   <item><description><c>*.example.com</c> — any subdomain of <c>example.com</c> (and <c>example.com</c> itself).</description></item>
/// </list>
/// The set is <b>data</b> (a CMOS setting that tracks Microsoft's monthly list, CM.BlackHole/docs/event-horizon-inspection.md
/// section 2), never hard-coded; only the rule that consults it is code. The set holds patterns only; no real company value lives
/// in the repository (C9).
///
/// Both sides are strict ASCII LDH names (RFC 1123 labels of letters, digits and hyphens, as a TLS server name is, RFC 6066):
/// <list type="bullet">
///   <item><description>A <b>pattern</b> that is not one (a typo, a URL, a CIDR, a non-ASCII letter) throws when the set is built, so the
///   feed job fails visibly instead of a Microsoft endpoint silently losing its bypass (GRB-0002 finding B4).</description></item>
///   <item><description>A <b>host</b> that is not one never matches: a look-alike such as a KELVIN SIGN for <c>k</c>, which the invariant
///   lower-casing would fold to ASCII, must not inherit a bypass (GRB-0002 finding B2). One trailing dot (the absolute form) is accepted.</description></item>
/// </list>
/// Matching is case-insensitive over ASCII only.
/// </summary>
public sealed class DomainSet
{
    const int MaxHostLength = 253;
    const int MaxLabelLength = 63;

    readonly HashSet<string> suffixes; // normalised registrable suffixes; a host matches when it equals one or ends with "." + one

    public DomainSet(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        suffixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in patterns)
        {
            var p = raw ?? throw new ArgumentException("a bypass pattern is null", nameof(patterns));
            if (p.StartsWith("*.", StringComparison.Ordinal)) p = p[2..];
            // "*.example.com" and "example.com" both reduce to the suffix "example.com": subdomains and the apex match.
            var name = TryNormaliseName(p, allowTrailingDot: false)
                ?? throw new ArgumentException($"not a DNS name pattern (letters, digits, hyphens, dots, optional leading *.): '{raw}'", nameof(patterns));
            suffixes.Add(name);
        }
    }

    public static DomainSet Empty { get; } = new([]);

    /// <summary>True when <paramref name="host"/> equals a suffix in the set or is a subdomain of one. A host that is not an ASCII DNS name never matches.</summary>
    public bool Contains(string? host)
    {
        var h = TryNormaliseName(host, allowTrailingDot: true);
        if (h is null) return false;
        foreach (var suffix in suffixes)
        {
            if (h.Equals(suffix, StringComparison.Ordinal)) return true;
            if (h.Length > suffix.Length && h.EndsWith(suffix, StringComparison.Ordinal)
                && h[h.Length - suffix.Length - 1] == '.') return true;
        }
        return false;
    }

    /// <summary>Lower-cased ASCII LDH name, or null when <paramref name="value"/> is not one. No trimming: whitespace is not part of a name.</summary>
    internal static string? TryNormaliseName(string? value, bool allowTrailingDot)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var v = value;
        if (allowTrailingDot && v.Length > 1 && v[^1] == '.') v = v[..^1];
        if (v.Length == 0 || v.Length > MaxHostLength) return null;
        if (!CategoryTag.IsAscii(v)) return null;
        v = v.ToLowerInvariant();
        foreach (var label in v.Split('.'))
        {
            if (label.Length == 0 || label.Length > MaxLabelLength) return null;
            if (label[0] == '-' || label[^1] == '-') return null;
            foreach (var c in label)
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return null;
        }
        return v;
    }
}
