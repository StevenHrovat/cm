using System.Collections.Concurrent;

namespace CM.MilkyWay.Verify;

/// <summary>What Milky Way accepted, as counted (one value per handshake, per message kind of IM version 0, and per answered door frame).</summary>
public enum ImAccepted
{
    Session,
    Heartbeat,
    /// <summary>A door frame Doppler answered (DMP chapter 04, "The enrolment door").</summary>
    DoorFrame,
}

/// <summary>
/// The refusal log (DMP chapter 04, "The refusal log"): counts, never content. Every method takes enumerations only, so no subject,
/// token, payload, address or secret can reach a log by construction; <c>RefusalLogTests</c> checks this signature.
/// </summary>
public interface IImRefusalLog
{
    void Refused(ImRefusal reason);

    void Accepted(ImAccepted what);
}

/// <summary>The in-memory counters behind the refusal log; CMB reads a snapshot.</summary>
public sealed class ImCounters : IImRefusalLog
{
    readonly ConcurrentDictionary<ImRefusal, long> refused = new();
    readonly ConcurrentDictionary<ImAccepted, long> accepted = new();

    public void Refused(ImRefusal reason) => refused.AddOrUpdate(reason, 1, (_, n) => n + 1);

    public void Accepted(ImAccepted what) => accepted.AddOrUpdate(what, 1, (_, n) => n + 1);

    public long RefusedCount(ImRefusal reason) => refused.GetValueOrDefault(reason);

    public long RefusedCount(ImRefusalFamily family) => refused.Where(p => p.Key.Family() == family).Sum(p => p.Value);

    public long AcceptedCount(ImAccepted what) => accepted.GetValueOrDefault(what);

    public long TotalRefused => refused.Values.Sum();

    /// <summary>The counts as a table, for the CLI and for CMB: reason and number, nothing else.</summary>
    public IReadOnlyList<(string What, long Count)> Snapshot() =>
        accepted.OrderBy(p => p.Key).Select(p => ("accepted " + p.Key, p.Value))
            .Concat(refused.OrderBy(p => p.Key).Select(p => ("refused " + p.Key, p.Value)))
            .ToArray();
}
