using cm.os.Health;
using Im.Trust;
using CM.MilkyWay.Board;
using CM.MilkyWay.Gate;
using CM.MilkyWay.Verify;

namespace CM.MilkyWay.Health;

/// <summary>
/// Milky Way's side of the mutual health watch with Doppler (<c>cm.os.Health</c>): a report signed with the gate key that
/// carries counts only (refusals by reason, accepted sessions and messages, board entries) and the ids of the tokens Milky Way
/// accepted since its last report, so Doppler can raise an alarm for any id it never issued. It sits in front of the board as the
/// gate's sink, so every verified message passes through it; the gate tells it the token of every established session too.
/// </summary>
public sealed class MilkyWayHealth(IImSigner gateKey, ImCounters counters, HeartbeatBoard board, TimeProvider clock, int maxTokenIdsPerReport = 10_000)
    : IHealthSource, IVerifiedMessageSink, ITokenAcceptanceSink
{
    /// <summary>The component name Doppler's watch expects.</summary>
    public const string ComponentName = "milkyway";

    readonly Lock gate = new();
    readonly HashSet<string> acceptedTokenIds = new(StringComparer.Ordinal);
    long sequence;
    long droppedTokenIds;
    long replayFullAtLastReport;

    public void Deliver(VerifiedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        TokenAccepted(message.TokenId);
        board.Deliver(message);
    }

    public void TokenAccepted(string tokenId)
    {
        lock (gate)
        {
            if (acceptedTokenIds.Count < maxTokenIdsPerReport) acceptedTokenIds.Add(tokenId);
            else if (!acceptedTokenIds.Contains(tokenId)) droppedTokenIds++;
        }
    }

    /// <summary>
    /// The signed report. Degraded when the replay memory refused messages since the last report (it is full and fails closed);
    /// healthy otherwise. The token id list starts empty again after each report.
    /// </summary>
    public SignedHealthReport Report()
    {
        string[] tokenIds;
        long dropped, seq, replayFull = counters.RefusedCount(ImRefusal.ReplayCacheFull);
        HealthState state;
        lock (gate)
        {
            tokenIds = acceptedTokenIds.Order(StringComparer.Ordinal).ToArray();
            acceptedTokenIds.Clear();
            dropped = droppedTokenIds;
            droppedTokenIds = 0;
            seq = ++sequence;
            state = replayFull > replayFullAtLastReport ? HealthState.Degraded : HealthState.Healthy;
            replayFullAtLastReport = replayFull;
        }

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["board.entries"] = board.Entries.Count,
            ["accepted.session"] = counters.AcceptedCount(ImAccepted.Session),
            ["accepted.heartbeat"] = counters.AcceptedCount(ImAccepted.Heartbeat),
        };
        foreach (var reason in Enum.GetValues<ImRefusal>())
            if (counters.RefusedCount(reason) is var n and > 0) counts["refused." + reason.ToString().ToLowerInvariant()] = n;
        if (dropped > 0) counts["health.tokenids.dropped"] = dropped;

        return SignedHealthReport.Sign(new HealthReport(ComponentName, state, clock.GetUtcNow(), seq, counts, tokenIds), gateKey);
    }
}
