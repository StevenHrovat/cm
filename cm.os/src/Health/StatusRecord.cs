namespace cm.os.Health;

/// <summary>The four states a component reports about itself (Observatory specification, section 4, "Observe").</summary>
public enum ObservedState
{
    /// <summary>Doing its job and every declared invariant holds.</summary>
    Healthy,

    /// <summary>A person has to look: a declared invariant is broken or the component cannot do its job.</summary>
    Attention,

    /// <summary>Doing its job in part (one peer unreachable, one source stale).</summary>
    Degraded,

    /// <summary>Switched off on purpose (a setting, a pause, a safe state). Not a fault.</summary>
    Off,
}

/// <summary>What an observer shows: the four reported states plus the two that only a reader can say.</summary>
public enum ShownState
{
    Healthy,
    Attention,
    Degraded,
    Off,

    /// <summary>The record is older than the reader allows. Silence is never Healthy.</summary>
    Stale,

    /// <summary>No record exists: the component publishes none, or nothing reads it yet, or it is not built.</summary>
    NotMonitored,
}

/// <summary>
/// The record a component publishes about itself and an observer reads (the "status overview contract" of the Pictor design, defined in
/// Observatory specification section 4). Counts, never content (CMB rule): <see cref="Counts"/> are named counters. <see cref="At"/> is the time on
/// the component's own clock. <see cref="Reason"/> is a stable lower-case word with dashes (<c>reachable</c>, <c>replay-memory-full</c>), the key an
/// observer and a test can match; <see cref="Text"/> is one plain sentence for a person, with no secret and no personal data.
/// </summary>
public sealed record StatusRecord(
    string Component,
    ObservedState State,
    string Reason,
    string Text,
    DateTimeOffset At,
    DateTimeOffset? Since,
    string Build,
    IReadOnlyDictionary<string, long> Counts)
{
    /// <summary>A record without counters, for the many components that have none yet.</summary>
    public static StatusRecord Of(string component, ObservedState state, string reason, string text, DateTimeOffset at, DateTimeOffset? since = null, string build = "") =>
        new(component, state, reason, text, at, since, build, new Dictionary<string, long>());
}

/// <summary>What an observer shows for one component: the state, a reason word, the age of the record on the observer's clock.</summary>
public readonly record struct ShownStatus(ShownState State, string Reason, string Text, TimeSpan? Age);

/// <summary>
/// How an observer turns a record (or the lack of one) into what it shows. Pure: the clock is a parameter. The rules are the specification's:
/// no record is NotMonitored, a record older than the limit is Stale, a record from the future beyond the allowed skew is Attention (a clock that cannot be
/// trusted is a finding, not a healthy sign), anything else is the record's own state.
/// </summary>
public static class StatusReading
{
    /// <summary>Default clock skew an observer tolerates between a component's clock and its own.</summary>
    public static readonly TimeSpan DefaultSkew = TimeSpan.FromMinutes(2);

    public static ShownStatus Show(StatusRecord? record, DateTimeOffset now, TimeSpan staleAfter, TimeSpan? skew = null)
    {
        if (record is null)
            return new(ShownState.NotMonitored, "no-record", "No status record is published, or nothing reads it yet.", null);

        var age = now - record.At;
        if (age < -(skew ?? DefaultSkew))
            return new(ShownState.Attention, "clock-ahead", "The record is dated in the future; the component's clock or the reader's clock is wrong.", age);
        if (age > staleAfter)
            return new(ShownState.Stale, "stale", $"The last record is older than the allowed {Format(staleAfter)}.", age);

        return new(From(record.State), record.Reason, record.Text, age < TimeSpan.Zero ? TimeSpan.Zero : age);
    }

    public static ShownState From(ObservedState state) => state switch
    {
        ObservedState.Healthy => ShownState.Healthy,
        ObservedState.Attention => ShownState.Attention,
        ObservedState.Degraded => ShownState.Degraded,
        ObservedState.Off => ShownState.Off,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The reported state for the older <see cref="HealthState"/> vocabulary: Unhealthy is a person's job, so Attention.</summary>
    public static ObservedState From(HealthState state) => state switch
    {
        HealthState.Healthy => ObservedState.Healthy,
        HealthState.Degraded => ObservedState.Degraded,
        HealthState.Unhealthy => ObservedState.Attention,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The shown state for what a component knows about its peer (<see cref="PeerState"/>): Untrusted needs a person; Unknown is nobody's record.</summary>
    public static ShownState From(PeerState state) => state switch
    {
        PeerState.Healthy => ShownState.Healthy,
        PeerState.Degraded => ShownState.Degraded,
        PeerState.Unhealthy => ShownState.Attention,
        PeerState.Untrusted => ShownState.Attention,
        PeerState.Stale => ShownState.Stale,
        PeerState.Unknown => ShownState.NotMonitored,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{span.TotalHours:0.#} h" : span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#} min" : $"{span.TotalSeconds:0} s";
}
