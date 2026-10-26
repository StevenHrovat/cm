using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CM.BlackHole.Doppler.Tpm;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Tpm;

/// <summary>
/// A throw-away software TPM 2.0 (swtpm on libtpms) in a temporary folder, reached over its TCP port: the real TPM implementation the
/// agent's TPM commands are tested against. Never a machine's TPM. CI installs swtpm and sets <c>CM_REQUIRE_SWTPM=1</c>, so there a
/// missing swtpm fails; on a workstation without it the tests are skipped and say why.
/// </summary>
public sealed class Swtpm : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("swtpm-").FullName;
    readonly TcpClient client = new();
    readonly int pid;

    public Swtpm()
    {
        var port = FreePort();
        var pidFile = Path.Combine(folder, "swtpm.pid");
        var start = new ProcessStartInfo(Executable!)
        {
            ArgumentList =
            {
                "socket", "--tpm2", "--tpmstate", $"dir={folder}",
                "--server", $"type=tcp,port={port},bindaddr=127.0.0.1", "--ctrl", $"type=tcp,port={port + 1},bindaddr=127.0.0.1",
                "--flags", "not-need-init,startup-clear", "--daemon", "--pid", $"file={pidFile}",
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        // With --daemon, swtpm returns once its sockets listen, so connecting right after needs no waiting.
        using (var process = Process.Start(start)!)
        {
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("swtpm did not start: " + process.StandardError.ReadToEnd());
        }
        pid = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        client.Connect(IPAddress.Loopback, port);
        Transport = new StreamTpmTransport(client.GetStream());
    }

    public StreamTpmTransport Transport { get; }

    public static string? Executable { get; } = Find("swtpm");

    public static bool Required => Environment.GetEnvironmentVariable("CM_REQUIRE_SWTPM") == "1";

    static string? Find(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    static int FreePort()
    {
        // Two adjacent free ports (server and control); a port taken between this probe and swtpm's bind fails the start loudly.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            if (port >= 65535) continue;
            using var next = new TcpListener(IPAddress.Loopback, port + 1);
            try { next.Start(); next.Stop(); return port; }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("no two adjacent free ports");
    }

    public void Dispose()
    {
        Transport.Dispose();
        client.Dispose();
        try { Process.GetProcessById(pid).Kill(); }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>A test that needs swtpm: skipped when it is missing, unless CI requires it.</summary>
public sealed class SwtpmFactAttribute : FactAttribute
{
    public SwtpmFactAttribute()
    {
        if (Swtpm.Executable is null && !Swtpm.Required) Skip = "swtpm is not installed (CI installs it and requires these tests)";
    }
}
