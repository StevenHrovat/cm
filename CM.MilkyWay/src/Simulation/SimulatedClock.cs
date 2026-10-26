namespace CM.MilkyWay.Simulation;

/// <summary>
/// Simulated time for the sandbox world: it moves only when told to. Every part of Milky Way reads time through a <see cref="TimeProvider"/>,
/// so the simulator can run a week in a test. Timers made from this clock (the deadlines of <see cref="Transport.GateTransport"/>) fire
/// inside <see cref="Advance"/> when the clock passes their due time, never on their own, so a test that times out a connection waits on a
/// condition, not on the wall clock.
/// </summary>
public sealed class SimulatedClock(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;
    readonly Lock gate = new();
    readonly List<SimulatedTimer> timers = [];

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return now;
    }

    /// <summary>Moves the clock and fires, in due order and on the calling thread, every timer that has come due.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (gate)
        {
            now += by;
            target = now;
        }
        while (true)
        {
            SimulatedTimer? due;
            lock (gate) due = timers.Where(t => t.DueAt is { } at && at <= target).OrderBy(t => t.DueAt).FirstOrDefault();
            if (due is null) return;
            due.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new SimulatedTimer(this, callback, state);
        lock (gate) timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Timers that are armed now (for tests that check a deadline exists before they move the clock).</summary>
    public int ArmedTimers
    {
        get { lock (gate) return timers.Count(t => t.DueAt is not null); }
    }

    sealed class SimulatedTimer(SimulatedClock clock, TimerCallback callback, object? state) : ITimer
    {
        TimeSpan period = Timeout.InfiniteTimeSpan;

        /// <summary>When the timer fires next; null when it is disarmed or disposed. Read and written under the clock's lock.</summary>
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.gate)
            {
                if (!clock.timers.Contains(this)) return false;
                this.period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
                return true;
            }
        }

        public void Fire()
        {
            lock (clock.gate)
            {
                if (DueAt is not { } at) return;
                DueAt = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : at + period;
            }
            callback(state);
        }

        public void Dispose()
        {
            lock (clock.gate)
            {
                DueAt = null;
                clock.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
