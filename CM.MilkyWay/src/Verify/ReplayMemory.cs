namespace CM.MilkyWay.Verify;

public enum ReplayOutcome
{
    New,
    Seen,
    Full,
}

/// <summary>
/// Remembers ids until they expire (verify rule 16). An entry is forgotten only once its expiry is in the past, and the caller sets
/// the expiry to "sent at plus the clock skew", the moment from which the freshness rule refuses the message anyway; so nothing
/// inside the window is forgotten early. When the memory is full and nothing has expired it answers <see cref="ReplayOutcome.Full"/>
/// (fail closed) instead of forgetting.
/// </summary>
public sealed class ReplayMemory(int capacity)
{
    readonly Dictionary<string, long> expiries = new(StringComparer.Ordinal);
    readonly PriorityQueue<string, long> byExpiry = new();
    readonly Lock gate = new();

    public int Count
    {
        get { lock (gate) return expiries.Count; }
    }

    /// <param name="scope">Who the id belongs to (the sender's key id), so two senders never collide.</param>
    /// <param name="id">The message id or the knock nonce.</param>
    /// <param name="expiresAt">Unix milliseconds after which the id may be forgotten.</param>
    /// <param name="now">Unix milliseconds, the verifier's time.</param>
    public ReplayOutcome TryRemember(ReadOnlySpan<byte> scope, ReadOnlySpan<byte> id, long expiresAt, long now)
    {
        var key = Convert.ToHexString(scope) + ":" + Convert.ToHexString(id);
        lock (gate)
        {
            while (byExpiry.TryPeek(out var oldest, out var expiry) && expiry < now)
            {
                byExpiry.Dequeue();
                if (expiries.TryGetValue(oldest, out var current) && current == expiry) expiries.Remove(oldest);
            }
            if (expiries.ContainsKey(key)) return ReplayOutcome.Seen;
            if (expiries.Count >= capacity) return ReplayOutcome.Full;
            expiries[key] = expiresAt;
            byExpiry.Enqueue(key, expiresAt);
            return ReplayOutcome.New;
        }
    }
}
