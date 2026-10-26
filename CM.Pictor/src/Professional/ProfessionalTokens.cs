using System.Globalization;

namespace CM.Pictor.Professional;

/// <summary>
/// The colour tokens of <c>cm.pictor.professional</c> in dark mode: dark greys, a little playful. Every colour a
/// module uses is one of these tokens, never a literal in a view. The set is checked by <see cref="HueFence"/> in the tests.
/// </summary>
public static class ProfessionalTokens
{
    /// <summary>Token name (CSS custom property without the leading dashes) to colour.</summary>
    public static IReadOnlyDictionary<string, string> Dark { get; } = new Dictionary<string, string>
    {
        ["bg"] = "#15171c",
        ["surface"] = "#1d2027",
        ["surface-raised"] = "#252932",
        ["border"] = "#343a46",
        ["text"] = "#e7e9ee",
        ["text-muted"] = "#9aa2b0",
        ["accent"] = "#46b8c6",
        ["state-arrived"] = "#4fb47a",
        ["state-partly"] = "#6cb4ff",
        ["state-missing"] = "#8a95a8",
        ["state-not-yet-due"] = "#667084",
        ["state-stale"] = "#6ac4e0",
        ["state-failing"] = "#e5524a",
    };
}

/// <summary>
/// The no-orange rule of enforced as a test and not as a comment: no token whose hue lies between 15° and 50° with a
/// saturation above 20 % (the band that reads as orange or amber). Lifted only when a light mode exists and the user chose it.
/// </summary>
public static class HueFence
{
    public const double OrangeFromDegrees = 15;
    public const double OrangeToDegrees = 50;
    public const double MinSaturation = 0.20;

    /// <summary>Hue in degrees (0–360) and saturation (0–1) of a <c>#rrggbb</c> colour, HSL model.</summary>
    public static (double Hue, double Saturation) Hsl(string hex)
    {
        var r = Channel(hex, 1);
        var g = Channel(hex, 3);
        var b = Channel(hex, 5);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60, s);
    }

    public static bool IsOrange(string hex)
    {
        var (hue, saturation) = Hsl(hex);
        return hue is >= OrangeFromDegrees and <= OrangeToDegrees && saturation > MinSaturation;
    }

    static double Channel(string hex, int start) =>
        int.Parse(hex.AsSpan(start, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255.0;
}
