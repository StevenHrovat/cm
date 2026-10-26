using System.Collections.Concurrent;
using Im;
using Im.Trust;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Verify;

namespace CM.MilkyWay.Board;

/// <summary>One device's last verified heartbeat. <see cref="Verified"/> is always true: the board has no other input than verified messages.</summary>
public sealed record HeartbeatEntry(string Subject, EnrolledRole Role, ulong Sequence, ImHeartbeatStatus Status, string AgentVersion, DateTimeOffset SentAt, DateTimeOffset ReceivedAt)
{
    public bool Verified => true;
}

/// <summary>
/// The heartbeat board (DMP chapter 05): the last verified heartbeat per device subject, the newest by sent-at time. It is what the
/// the operations master board shows for Promise B once the listener exists.
/// </summary>
public sealed class HeartbeatBoard : IVerifiedMessageSink
{
    readonly ConcurrentDictionary<string, HeartbeatEntry> entries = new(StringComparer.Ordinal);

    public void Deliver(VerifiedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Heartbeat is not { } beat) return;
        var entry = new HeartbeatEntry(message.Subject, message.Role, beat.Sequence, beat.Status, beat.AgentVersion, message.SentAt, message.ReceivedAt);
        entries.AddOrUpdate(message.Subject, entry, (_, old) => entry.SentAt >= old.SentAt ? entry : old);
    }

    public HeartbeatEntry? Find(string subject) => entries.TryGetValue(subject, out var entry) ? entry : null;

    public IReadOnlyList<HeartbeatEntry> Entries => entries.Values.OrderBy(e => e.Subject, StringComparer.Ordinal).ToArray();
}
