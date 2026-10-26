using Xunit;
using cm.os.Health;

namespace cm.os.Tests.Health;

public class StatusRecordTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 25, 18, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(15);

    static StatusRecord Record(ObservedState state, TimeSpan age) =>
        StatusRecord.Of("c", state, "reachable", "ok", Now - age);

    [Fact]
    public void No_record_is_not_monitored_never_healthy()
    {
        var shown = StatusReading.Show(null, Now, Limit);
        Assert.Equal(ShownState.NotMonitored, shown.State);
        Assert.Equal("no-record", shown.Reason);
        Assert.Null(shown.Age);
    }

    [Theory]
    [InlineData(ObservedState.Healthy, ShownState.Healthy)]
    [InlineData(ObservedState.Attention, ShownState.Attention)]
    [InlineData(ObservedState.Degraded, ShownState.Degraded)]
    [InlineData(ObservedState.Off, ShownState.Off)]
    public void A_fresh_record_shows_its_own_state_and_reason(ObservedState reported, ShownState expected)
    {
        var shown = StatusReading.Show(Record(reported, TimeSpan.FromMinutes(1)), Now, Limit);
        Assert.Equal(expected, shown.State);
        Assert.Equal("reachable", shown.Reason);
        Assert.Equal(TimeSpan.FromMinutes(1), shown.Age);
    }

    [Fact]
    public void A_record_exactly_at_the_limit_is_still_fresh_and_one_second_over_is_stale()
    {
        Assert.Equal(ShownState.Healthy, StatusReading.Show(Record(ObservedState.Healthy, Limit), Now, Limit).State);
        var stale = StatusReading.Show(Record(ObservedState.Healthy, Limit + TimeSpan.FromSeconds(1)), Now, Limit);
        Assert.Equal(ShownState.Stale, stale.State);
        Assert.Equal("stale", stale.Reason);
    }

    [Fact]
    public void A_stale_record_is_stale_whatever_it_said()
    {
        Assert.Equal(ShownState.Stale, StatusReading.Show(Record(ObservedState.Off, TimeSpan.FromHours(3)), Now, Limit).State);
    }

    [Fact]
    public void A_record_from_the_future_beyond_the_skew_is_attention_and_within_it_is_fresh()
    {
        var ahead = StatusReading.Show(Record(ObservedState.Healthy, -TimeSpan.FromMinutes(10)), Now, Limit);
        Assert.Equal(ShownState.Attention, ahead.State);
        Assert.Equal("clock-ahead", ahead.Reason);

        var within = StatusReading.Show(Record(ObservedState.Healthy, -TimeSpan.FromSeconds(30)), Now, Limit);
        Assert.Equal(ShownState.Healthy, within.State);
        Assert.Equal(TimeSpan.Zero, within.Age);
    }

    [Theory]
    [InlineData(HealthState.Healthy, ObservedState.Healthy)]
    [InlineData(HealthState.Degraded, ObservedState.Degraded)]
    [InlineData(HealthState.Unhealthy, ObservedState.Attention)]
    public void The_older_health_vocabulary_maps_onto_the_four_states(HealthState old, ObservedState expected) =>
        Assert.Equal(expected, StatusReading.From(old));

    [Theory]
    [InlineData(PeerState.Unknown, ShownState.NotMonitored)]
    [InlineData(PeerState.Healthy, ShownState.Healthy)]
    [InlineData(PeerState.Degraded, ShownState.Degraded)]
    [InlineData(PeerState.Unhealthy, ShownState.Attention)]
    [InlineData(PeerState.Stale, ShownState.Stale)]
    [InlineData(PeerState.Untrusted, ShownState.Attention)]
    public void What_a_component_knows_about_its_peer_maps_onto_the_shown_states(PeerState peer, ShownState expected) =>
        Assert.Equal(expected, StatusReading.From(peer));

    [Fact]
    public void Every_value_of_every_older_enum_is_mapped()
    {
        foreach (var state in Enum.GetValues<HealthState>()) _ = StatusReading.From(state);
        foreach (var state in Enum.GetValues<PeerState>()) _ = StatusReading.From(state);
        foreach (var state in Enum.GetValues<ObservedState>()) _ = StatusReading.From(state);
    }
}
