using System.Diagnostics;

namespace Cm.Gravity;

/// <summary>What a run of an outside program answered: its exit code and its stdout and stderr as one text.</summary>
sealed record RunResult(int Exit, string Output)
{
    public bool AllProofsCheck => Exit == 0 && Output.Contains("ALL PROOFS CHECK", StringComparison.Ordinal);
}

/// <summary>
/// The Bend 2 command (HigherOrderCO/Bend, pinned release in CM.BlackHole/docs/gravity.md) and Node.js, which runs the model compiled
/// to JavaScript. <c>CM_BEND</c> is the command line of Bend (for example <c>bun /opt/bend/bend2/main.ts</c>), else <c>bend</c> on the
/// PATH; <c>CM_NODE</c> is Node, else <c>node</c>. Neither is a CM dependency: without them the gravity step says it was skipped.
/// </summary>
sealed class Bend(string[] bend, string[] node)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    public static Bend? Find()
    {
        var bend = Command(Environment.GetEnvironmentVariable("CM_BEND"), "bend");
        var node = Command(Environment.GetEnvironmentVariable("CM_NODE"), "node");
        return bend is null || node is null ? null : new Bend(bend, node);
    }

    static string[]? Command(string? configured, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in OperatingSystem.IsWindows() ? new[] { fallback + ".exe", fallback + ".cmd" } : [fallback])
                if (File.Exists(Path.Combine(dir, name))) return [Path.Combine(dir, name)];
        return null;
    }

    /// <summary>Runs Bend on a file in <paramref name="dir"/>: checks it, runs its <c>main</c>, or with extra options checks or compiles it.</summary>
    public RunResult Run(string dir, params string[] args) => Start(bend, dir, args);

    public RunResult Node(string dir, string script) => Start(node, dir, [script]);

    static RunResult Start(string[] command, string dir, string[] args)
    {
        var info = new ProcessStartInfo(command[0]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in command.Skip(1).Concat(args)) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException(command[0] + " did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Limit))
        {
            process.Kill(entireProcessTree: true);
            return new RunResult(-1, "timed out after " + Limit);
        }
        return new RunResult(process.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
