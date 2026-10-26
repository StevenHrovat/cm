using cm.os.Inspection;

namespace CM.BlackHole.EventHorizon;

/// <summary>
/// The decision layer of Event Horizon (CM.BlackHole/docs/event-horizon-inspection.md). It decides, per connection, the
/// deepest inspection depth allowed, in a fixed order of rules that fails closed. It never reads payload; it only decides.
///
/// The order matters and is the heart of the design, so it is explicit here and each step has a test that produces it:
/// <list type="number">
///   <item><description>EH-DEFAULT: switch off → <c>Bypass(SwitchOff)</c>. The shipped default inspects nothing.</description></item>
///   <item><description>EH-BYPASS-1: Microsoft-managed destination → <c>Bypass(MicrosoftManaged)</c>. Never inspected, by rule.</description></item>
///   <item><description>Uncategorised destination, or a category that is not a valid tag (<see cref="CategoryTag"/>) → <c>Metadata(PinnedUncategorised)</c>. Never content: fail closed.</description></item>
///   <item><description>EH-BYPASS-2: sensitive category → <c>Bypass(SensitiveCategory)</c>. We choose never to inspect it.</description></item>
///   <item><description>Content-enabled category → <c>Content(ContentPermitted)</c>. The only path to payload.</description></item>
///   <item><description>Otherwise → <c>Metadata(MetadataOnly)</c>. The default inspection depth for a categorised, non-sensitive destination.</description></item>
/// </list>
/// Steps 2 and 3 are checked before 4 on purpose: a Microsoft-managed host is bypassed even if someone miscategorises it,
/// and an uncategorised host can never reach content even if the switch is on. The category is normalised here, once, so the
/// comparison never depends on how the resolver or the settings spelt it (GRB-0002 findings A4, A5).
/// </summary>
public sealed class EventHorizonPolicy(InspectionSettings settings) : IInspectionPolicy
{
    readonly InspectionSettings settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public InspectionVerdict Decide(ConnectionFacts connection)
    {
        // 1. EH-DEFAULT: the governance switch is off → nothing is inspected.
        if (!settings.InspectionEnabled)
            return InspectionVerdict.Bypass(InspectionReason.SwitchOff);

        // 2. EH-BYPASS-1: Microsoft-managed destinations are never inspected, whatever their category says.
        if (settings.MicrosoftManaged.Contains(connection.DestinationHost))
            return InspectionVerdict.Bypass(InspectionReason.MicrosoftManaged);

        // 3. No category known, or not a category at all → metadata only, never content (fail closed).
        var category = CategoryTag.TryNormalise(connection.Category);
        if (category is null)
            return InspectionVerdict.Metadata(InspectionReason.PinnedUncategorised);

        // 4. EH-BYPASS-2: a sensitive category is never inspected, even with the switch on.
        if (settings.SensitiveCategories.Contains(category))
            return InspectionVerdict.Bypass(InspectionReason.SensitiveCategory);

        // 5. Content inspection is reachable only for an explicitly content-enabled category.
        if (settings.ContentEnabledCategories.Contains(category))
            return InspectionVerdict.Content(InspectionReason.ContentPermitted);

        // 6. Default inspection depth: metadata only.
        return InspectionVerdict.Metadata(InspectionReason.MetadataOnly);
    }
}
