using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using Xunit;

namespace CM.BlackHole.Tests.Doppler;

/// <summary>The agent owns port 138 and NetBIOS stays off (FCS-6).</summary>
public sealed class PortTests : IDisposable
{
    readonly DopplerWorld world = new();

    public void Dispose() => world.Dispose();

    [Fact]
    public void After_the_agent_starts_NetBIOS_cannot_start_and_port_138_is_the_agents()
    {
        var (agent, _, host) = world.NewMachine();
        Assert.Equal(AgentState.Started, agent.Start());

        Assert.False(SimulatedNetBios.TryStart(host));
        Assert.Equal(DopplerAgent.Owner, host.OwnerOf(Transport.Tcp, 138));
        Assert.Equal(DopplerAgent.Owner, host.OwnerOf(Transport.Udp, 138));
        Assert.Null(host.OwnerOf(Transport.Udp, 137));
        Assert.Null(host.OwnerOf(Transport.Tcp, 139));
    }

    [Fact]
    public void A_NetBIOS_that_started_first_is_switched_off_and_loses_port_138()
    {
        var (agent, _, host) = world.NewMachine();
        Assert.True(SimulatedNetBios.TryStart(host));
        Assert.Equal(SimulatedNetBios.Owner, host.OwnerOf(Transport.Udp, 138));

        Assert.Equal(AgentState.Started, agent.Start());
        Assert.Equal(DopplerAgent.Owner, host.OwnerOf(Transport.Udp, 138));
        Assert.False(host.NetBiosEnabled);
    }

    [Fact]
    public void Another_process_on_port_138_stops_the_agent_and_it_does_not_enrol()
    {
        var (agent, _, host) = world.NewMachine();
        host.TryBind(Transport.Tcp, 138, "something-else");

        Assert.Equal(AgentState.PortConflict, agent.Start());
        Assert.False(agent.Enrol());
        Assert.Equal(0, world.Server.Registry.Count);
    }
}
