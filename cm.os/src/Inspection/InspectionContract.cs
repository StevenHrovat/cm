namespace cm.os.Inspection;

/// <summary>
/// The contract for network inspection of CMOS-managed devices (CM.BlackHole/docs/event-horizon-inspection.md).
/// It lives in <c>cm.os</c> so a future data plane (a Milky Way carriage) can ask the decision without referencing
/// Black Hole (constitution C1): the carriage holds no policy, it asks an <see cref="IInspectionPolicy"/>.
///
/// Security context: defensive inspection of the operating organisation's own managed devices, under the charter in CLAUDE.md. The
/// decision layer never reads payload; it only decides whether a connection may be inspected at all, and to what depth.
/// </summary>

/// <summary>How deep a connection may be looked at. The order is the depth order: later values allow more.</summary>
public enum InspectionDepth
{
    /// <summary>Pass through untouched: no TLS termination, no protocol downgrade, no payload, no metadata record.</summary>
    Bypass = 0,

    /// <summary>Connection metadata only (destination category, timing, byte counts). Never payload.</summary>
    InspectMetadata = 1,

    /// <summary>TLS may be terminated with the inspection authority and payload read. The deepest, most gated depth.</summary>
    InspectContent = 2,
}

/// <summary>
/// Why a connection got the depth it got. The reason is what the counts-only telemetry (CMB) is keyed by, together with the
/// category and the depth; it never carries a host, a subject or any content.
/// </summary>
public enum InspectionReason
{
    /// <summary>The governance switch is off (the shipped default): everything is bypassed.</summary>
    SwitchOff,

    /// <summary>A Microsoft-managed endpoint (M365 Optimize/Allow, Intune/Defender/Autopilot): must never be inspected (EH-BYPASS-1).</summary>
    MicrosoftManaged,

    /// <summary>A category in the sensitive set (banking, health, personal webmail, ...): we choose never to inspect it (EH-BYPASS-2).</summary>
    SensitiveCategory,

    /// <summary>The destination has no known category: fail closed, no content inspection until it is categorised (EH-DEFAULT).</summary>
    PinnedUncategorised,

    /// <summary>Metadata-only is the default inspection depth for a categorised, non-sensitive destination.</summary>
    MetadataOnly,

    /// <summary>Content inspection is permitted: switch on, destination categorised, category outside both bypass sets and content-enabled.</summary>
    ContentPermitted,
}

/// <summary>
/// What the decision layer is told about a connection. It is metadata known before any payload is read: the destination
/// name (SNI/host) and the category the resolver tagged it with (null when unknown). No device subject, no payload.
/// </summary>
/// <param name="DestinationHost">The destination name as seen at connection time (SNI or host). Opaque to the policy.</param>
/// <param name="Category">The category the CMOS resolver/feed tagged the destination with, or null when uncategorised.</param>
public readonly record struct ConnectionFacts(string DestinationHost, string? Category);

/// <summary>The decision for one connection: a depth and the reason, for the data plane to act on and for CMB to count.</summary>
public readonly record struct InspectionVerdict(InspectionDepth Depth, InspectionReason Reason)
{
    public static InspectionVerdict Bypass(InspectionReason reason) => new(InspectionDepth.Bypass, reason);
    public static InspectionVerdict Metadata(InspectionReason reason) => new(InspectionDepth.InspectMetadata, reason);
    public static InspectionVerdict Content(InspectionReason reason) => new(InspectionDepth.InspectContent, reason);
}

/// <summary>
/// Decides, for one connection, the deepest inspection depth allowed. A data plane consults it per connection and may never
/// go deeper than the verdict. Fail-closed: when in doubt the verdict is <see cref="InspectionDepth.Bypass"/> or
/// <see cref="InspectionDepth.InspectMetadata"/>, never <see cref="InspectionDepth.InspectContent"/>.
/// </summary>
public interface IInspectionPolicy
{
    InspectionVerdict Decide(ConnectionFacts connection);
}
