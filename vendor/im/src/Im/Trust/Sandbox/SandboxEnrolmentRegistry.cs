using System.Collections.Concurrent;

namespace Im.Trust.Sandbox;

/// <summary>An in-memory <see cref="IEnrolmentRegistry"/> for tests and the simulator. Doppler's own registry has the same reading rules.</summary>
public sealed class SandboxEnrolmentRegistry : IEnrolmentRegistry
{
    readonly ConcurrentDictionary<string, EnrolmentRecord> records = new(StringComparer.Ordinal);

    public EnrolmentRecord? Find(string subject) => records.TryGetValue(subject, out var record) ? record : null;

    public void Put(EnrolmentRecord record) => records[record.Subject] = record;

    public void Revoke(string subject) => records.AddOrUpdate(subject, _ => throw new KeyNotFoundException(subject), (_, r) => r with { Revoked = true });
}
