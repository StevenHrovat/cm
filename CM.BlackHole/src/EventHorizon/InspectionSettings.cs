namespace CM.BlackHole.EventHorizon;

/// <summary>
/// Every value an administrator tunes for Event Horizon, with a default and a plain description (DMP chapter 10). The DPO
/// can read all of it; it is what the governance gate controls (CM.BlackHole/docs/event-horizon-inspection.md section 8).
///
/// The governance switch is <b>off</b> by default: with it off, every connection is bypassed (EH-DEFAULT, fail-closed).
/// The bypass set and the sensitive set are bypassed even when the switch is on; content inspection is reachable only for a
/// category that is categorised, in neither bypass set, and explicitly in <see cref="ContentEnabledCategories"/>.
///
/// Categories are normalised through <see cref="CategoryTag"/> when they are set, so a comparison never depends on the comparer
/// or the spelling a caller used, and the sensitive set is the <b>union</b> of what is given and <see cref="DefaultSensitiveCategories"/>:
/// configuration can add to it, never remove from it (GRB-0002 findings A2 to A4).
/// </summary>
public sealed class InspectionSettings
{
    IReadOnlySet<string> sensitive = DefaultSensitiveCategories;
    IReadOnlySet<string> contentEnabled = EmptySet;

    /// <summary>The one governance switch. Off: everything bypassed. Turning it on is a gated, audited action (section 8).</summary>
    public bool InspectionEnabled { get; init; }

    /// <summary>
    /// The Microsoft-managed destinations that must never be inspected (EH-BYPASS-1): every <c>required</c> endpoint of the M365
    /// endpoints web service (all categories, Optimize, Allow and Default alike; Microsoft's guidance applies to all required
    /// domains), plus the Intune, Defender for Endpoint, Autopilot, Store, Endpoint Privilege Management and attestation sets that
    /// Microsoft publishes outside that web service. A CMOS setting, updated monthly from those lists.
    /// </summary>
    public DomainSet MicrosoftManaged { get; init; } = DomainSet.Empty;

    /// <summary>
    /// Destination categories we choose never to inspect (EH-BYPASS-2): banking, health, government/tax, personal webmail,
    /// legal, works-council. Always contains <see cref="DefaultSensitiveCategories"/>: setting it adds to the floor, never removes.
    /// </summary>
    public IReadOnlySet<string> SensitiveCategories
    {
        get => sensitive;
        init
        {
            var set = CategoryTag.NormaliseSet(value);
            set.UnionWith(DefaultSensitiveCategories);
            sensitive = set;
        }
    }

    /// <summary>
    /// Categories for which content inspection is permitted when the switch is on. Default empty: content inspection is
    /// never the default for anything. Enabling a category is gated (owner + DPO + staff notice). Entries that are not a valid
    /// category tag are dropped: they could never match a resolver tag.
    /// </summary>
    public IReadOnlySet<string> ContentEnabledCategories
    {
        get => contentEnabled;
        init => contentEnabled = CategoryTag.NormaliseSet(value);
    }

    /// <summary>The sensitive set that ships on: these are bypassed by default (section 3) and cannot be configured away.</summary>
    public static IReadOnlySet<string> DefaultSensitiveCategories { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "banking-finance",
        "health-medical",
        "government-tax",
        "personal-webmail",
        "legal-counsel",
        "works-council",
    };

    static IReadOnlySet<string> EmptySet { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The settings page rows (key, default, description), shown by the administration tool (DMP chapter 10).</summary>
    public static IReadOnlyList<(string Key, string Default, string Description)> Describe() =>
    [
        ("eventhorizon.inspection.enabled", "false",
            "The one governance switch for network inspection of managed devices. Off: every connection is bypassed. Turning it on needs management and DPO sign-off and a staff notice."),
        ("eventhorizon.bypass.microsoftManaged", "(from the M365 endpoints web service, all required endpoints, plus the Intune, Defender, Autopilot, Store, EPM and attestation lists)",
            "Microsoft-managed destinations that are never inspected. Tracked monthly; inspecting them is unsupported and breaks the service. Every entry must be a plain DNS name or *.wildcard; a malformed entry fails the update."),
        ("eventhorizon.bypass.sensitiveCategories", "banking-finance, health-medical, government-tax, personal-webmail, legal-counsel, works-council",
            "Destination categories never inspected, even with the switch on. The DPO may add to this list; the default entries cannot be removed."),
        ("eventhorizon.content.enabledCategories", "(none)",
            "Categories for which payload inspection is allowed when the switch is on. Empty by default; adding one is gated (owner + DPO + staff notice)."),
    ];
}
