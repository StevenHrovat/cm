namespace CM.BlackHole.Doppler.Simulation;

/// <summary>
/// A simulated machine's network stack: who holds which port, and whether NetBIOS over TCP/IP is on. Enough to prove the agent's port
/// rule (FCS-6: the agent owns port 138 and NetBIOS is blocked there) without a real socket.
/// </summary>
public sealed class SimulatedDeviceHost : IDeviceHost
{
    readonly Dictionary<(Transport, int), string> bound = [];
    readonly Lock gate = new();

    public bool NetBiosEnabled { get; private set; } = true;

    public string? OwnerOf(Transport transport, int port)
    {
        lock (gate) return bound.TryGetValue((transport, port), out var owner) ? owner : null;
    }

    public bool TryBind(Transport transport, int port, string owner)
    {
        lock (gate)
        {
            if (bound.TryGetValue((transport, port), out var current)) return current == owner;
            bound[(transport, port)] = owner;
            return true;
        }
    }

    public void DisableNetBios()
    {
        lock (gate)
        {
            NetBiosEnabled = false;
            foreach (var key in bound.Where(b => b.Value == SimulatedNetBios.Owner).Select(b => b.Key).ToList()) bound.Remove(key);
        }
    }
}

/// <summary>The NetBIOS service of a simulated Windows machine: name service UDP 137, datagram service UDP 138, session service TCP 139.</summary>
public static class SimulatedNetBios
{
    public const string Owner = "netbios";

    /// <summary>Starts NetBIOS on the host, as Windows does at boot when NetBIOS over TCP/IP is on. False when it is off or a port is taken.</summary>
    public static bool TryStart(SimulatedDeviceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.NetBiosEnabled) return false;
        return host.TryBind(Transport.Udp, 137, Owner) & host.TryBind(Transport.Udp, 138, Owner) & host.TryBind(Transport.Tcp, 139, Owner);
    }
}
