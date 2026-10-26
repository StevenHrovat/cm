using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CM.BlackHole.Doppler;
using Microsoft.Win32;

namespace CM.BlackHole.Agent;

/// <summary>
/// The machine's network stack for the agent: NetBIOS over TCP/IP off on every interface, and port 138 (TCP and UDP) held
/// exclusively by the agent for as long as it runs. The installer applies NetBIOS off at once (it can restart the interfaces' NetBIOS
/// state through CIM) and keeps the old values for the uninstaller; the agent only makes sure it stays off, also on new interfaces.
/// </summary>
public sealed class WindowsDeviceHost : IDeviceHost, IDisposable
{
    const string Interfaces = @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces";
    const int NetBiosDisabled = 2;

    readonly Dictionary<(Transport, int), (Socket Socket, string Owner)> held = [];

    public void DisableNetBios()
    {
        using var interfaces = Registry.LocalMachine.OpenSubKey(Interfaces, writable: false);
        if (interfaces is null) return;
        foreach (var name in interfaces.GetSubKeyNames())
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{Interfaces}\{name}", writable: true);
            if (key is not null && key.GetValue("NetbiosOptions") is not NetBiosDisabled) key.SetValue("NetbiosOptions", NetBiosDisabled, RegistryValueKind.DWord);
        }
    }

    public bool TryBind(Transport transport, int port, string owner)
    {
        if (held.TryGetValue((transport, port), out var current)) return current.Owner == owner;
        var socket = transport == Transport.Tcp
            ? new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            : new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            held[(transport, port)] = (socket, owner);
            return true;
        }
        catch (SocketException)
        {
            socket.Dispose();
            return false;
        }
    }

    /// <summary>The owner when the agent holds the port; another process's ownership is not looked up (null).</summary>
    public string? OwnerOf(Transport transport, int port) => held.TryGetValue((transport, port), out var h) ? h.Owner : null;

    public void Dispose()
    {
        foreach (var (socket, _) in held.Values) socket.Dispose();
        held.Clear();
    }
}

/// <summary>What the agent reports and the TPM does not measure: BitLocker protection of the system drive, and the agent's own hash.</summary>
public static class WindowsReport
{
    static readonly Lazy<string> PackageHash = new(() =>
    {
        using var file = File.OpenRead(Environment.ProcessPath ?? throw new InvalidOperationException("no process path"));
        return Convert.ToHexStringLower(SHA256.HashData(file));
    });

    /// <summary>SHA-256 of the agent executable; the packaging script prints the same value for Doppler's AllowedAgentPackages.</summary>
    public static string AgentPackageSha256 => PackageHash.Value;

    public static ReportedState Read() => new(SystemDriveProtected(), AgentPackageSha256);

    /// <summary>True when BitLocker protects the system drive (Win32_EncryptableVolume.ProtectionStatus = 1).</summary>
    public static bool SystemDriveProtected()
    {
        var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
                $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'");
            foreach (var volume in searcher.Get())
                using (volume)
                    if (volume["ProtectionStatus"] is uint status) return status == 1;
        }
        catch (ManagementException) { }
        return false;
    }
}
