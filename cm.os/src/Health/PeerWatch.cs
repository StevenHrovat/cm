using Im.Trust;

namespace cm.os.Health;

/// <summary>What a component knows about its peer right now.</summary>
public enum PeerState
{
    /// <summary>No valid report seen yet.</summary>
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
    /// <summary>The last valid report is older than the threshold: the peer may be down or cut off.</summary>
    Stale,
    /// <summary>The last report failed the check (signature, chain, role, replay, clock): treat the peer as compromised until a valid report arrives and an admin looks.</summary>
    Untrusted,
}

/// <summary>Why a report was refused. Codes, never the report's content.</summary>
public enum ReportRefusal
{
    None,
    Chain,
    Signature,
    WrongComponent,
    Replay,
    FromTheFuture,
}

public sealed record PeerView(string Component, PeerState State, DateTimeOffset? LastSeen, HealthReport? Last, ReportRefusal LastRefusal, long Refusals, ChainRefusal LastChainRefusal);

/// <summary>
/// One side's watch on its peer. A report counts only when its signer chains to the configured roots with the expected
/// role, the signature holds, the component name is the expected one, its sequence number grows (no replay) and its time is not
/// ahead of the watcher's clock by more than the allowed skew. A refused report turns the view to <see cref="PeerState.Untrusted"/>.
/// </summary>
public sealed class PeerWatch(string component, TrustRole role, ITrustAnchors anchors, TimeProvider clock, TimeSpan staleAfter, TimeSpan allowedSkew)
{
    readonly Lock gate = new();
    HealthReport? last;
    DateTimeOffset? lastSeen;
    ReportRefusal lastRefusal;
    ChainRefusal lastChainRefusal;
    long refusals;
    bool untrusted;

    public ReportRefusal Observe(SignedHealthReport signed)
    {
        ArgumentNullException.ThrowIfNull(signed);
        var now = clock.GetUtcNow();
        var refusal = Check(signed, now, out var chain);
        lock (gate)
        {
            if (refusal != ReportRefusal.None)
            {
                refusals++;
                lastRefusal = refusal;
                lastChainRefusal = chain;
                untrusted = true;
                return refusal;
            }
            if (last is not null && signed.Report.Sequence <= last.Sequence)
            {
                refusals++;
                lastRefusal = ReportRefusal.Replay;
                untrusted = true;
                return ReportRefusal.Replay;
            }
            last = signed.Report;
            lastSeen = now;
            untrusted = false;
            return ReportRefusal.None;
        }
    }

    public PeerView View()
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            PeerState state;
            if (untrusted) state = PeerState.Untrusted;
            else if (last is null) state = PeerState.Unknown;
            else if (now - lastSeen!.Value > staleAfter) state = PeerState.Stale;
            else state = last.State switch
            {
                HealthState.Healthy => PeerState.Healthy,
                HealthState.Degraded => PeerState.Degraded,
                _ => PeerState.Unhealthy,
            };
            return new(component, state, lastSeen, last, lastRefusal, refusals, lastChainRefusal);
        }
    }

    ReportRefusal Check(SignedHealthReport signed, DateTimeOffset now, out ChainRefusal chain)
    {
        chain = ChainRefusal.None;
        var verdict = anchors.Validate(signed.Signer, signed.Chain, role, now);
        if (!verdict.Ok) { chain = verdict.Refusal; return ReportRefusal.Chain; }
        if (!ImSignature.Verify(signed.Signer, signed.Report.CanonicalBytes(), signed.Signature)) return ReportRefusal.Signature;
        if (!string.Equals(signed.Report.Component, component, StringComparison.Ordinal)) return ReportRefusal.WrongComponent;
        if (signed.Report.At - now > allowedSkew) return ReportRefusal.FromTheFuture;
        return ReportRefusal.None;
    }
}
