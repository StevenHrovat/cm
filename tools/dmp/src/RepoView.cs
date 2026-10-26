using System.Text.RegularExpressions;

namespace Cm.Dmp;

/// <summary>Read-only view of a checkout. Every rule reads the tree only through this type, so tests can build a tree in a temp folder.</summary>
public sealed partial class RepoView
{
    static readonly string[] SkippedDirectories = [".git", "bin", "obj", "TestResults", "node_modules"];

    readonly string root;

    public RepoView(string root, string? scrubListPath = null)
    {
        this.root = Path.GetFullPath(root);
        ScrubList = scrubListPath is not null && File.Exists(scrubListPath)
            ? File.ReadAllLines(scrubListPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray()
            : [];
    }

    /// <summary>Reads a file as it is on the base of the change (null when the file does not exist there); null when no base is known.
    /// Throws <see cref="BaseUnreadable"/> when git fails for any other reason, so a rule never mistakes an error for an absent file.
    /// Set by <see cref="WithGitBase"/>; tests set it directly.</summary>
    public Func<string, string?>? ReadBaseText { get; init; }

    /// <summary>Why the base of a pull request cannot be read; a rule that needs the base fails closed on it.</summary>
    public string? BaseError { get; init; }

    /// <summary>A view whose base is the exact base commit of the pull request. GitHub sets GITHUB_BASE_REF on a pull request and
    /// writes the event (with pull_request.base.sha) to GITHUB_EVENT_PATH; no workflow may set either (DMP-020). The base is that
    /// commit id, not a ref, so a workflow step cannot move it: a fetch can only bring the commit or fail. No base branch (a push, a
    /// local run) means no base; a pull request whose base commit cannot be read fails closed.</summary>
    public static RepoView WithGitBase(string root, string? scrubListPath, string? baseBranch, string? eventPath)
    {
        if (string.IsNullOrWhiteSpace(baseBranch)) return new RepoView(root, scrubListPath);
        var full = Path.GetFullPath(root);
        string? sha = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(eventPath ?? ""));
            sha = doc.RootElement.GetProperty("pull_request").GetProperty("base").GetProperty("sha").GetString();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
        if (sha is null || !CommitId().IsMatch(sha)) return new RepoView(root, scrubListPath) { BaseError = "the pull request event names no base commit" };
        if (!Git(full, "cat-file", "-e", sha + "^{commit}").Ok) return new RepoView(root, scrubListPath) { BaseError = $"the base commit {sha} is not in this checkout; the verify workflow must fetch it" };
        return new RepoView(root, scrubListPath) { ReadBaseText = path => ReadAtCommit(full, sha, path) };
    }

    // ls-tree answers "absent" with exit 0 and no output; anything else that fails is an error, never an absent file.
    static string? ReadAtCommit(string root, string sha, string path)
    {
        var (listed, entry) = Git(root, "ls-tree", sha, "--", path);
        if (!listed) throw new BaseUnreadable($"git ls-tree {sha} {path} failed");
        if (entry.Trim().Length == 0) return null;
        var (shown, text) = Git(root, "show", $"{sha}:{path}");
        return shown ? text : throw new BaseUnreadable($"git show {sha}:{path} failed");
    }

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$")]
    private static partial Regex CommitId();

    static (bool Ok, string Output) Git(string root, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(root);
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, output);
    }

    /// <summary>Entries of the scrub list supplied by the pipeline (values that must never appear in the tree). Empty when none is supplied.</summary>
    public IReadOnlyList<string> ScrubList { get; }

    public bool Exists(string relative) => File.Exists(Full(relative)) || Directory.Exists(Full(relative));

    public string? ReadText(string relative) => File.Exists(Full(relative)) ? File.ReadAllText(Full(relative)) : null;

    public byte[] ReadBytes(string relative) => File.ReadAllBytes(Full(relative));

    /// <summary>All files below <paramref name="relativeDir"/> (the whole tree when empty), skipping build output and .git.</summary>
    public IEnumerable<string> Files(string relativeDir = "")
    {
        var start = Full(relativeDir);
        if (!Directory.Exists(start)) yield break;
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.GetDirectories(dir))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(sub))) pending.Push(sub);
            }
            foreach (var file in Directory.GetFiles(dir)) yield return Relative(file);
        }
    }

    public IEnumerable<string> RootDirectories() =>
        Directory.GetDirectories(root).Select(d => Path.GetFileName(d)).Where(n => !SkippedDirectories.Contains(n)).OrderBy(n => n, StringComparer.Ordinal);

    /// <summary>Text of a file, or null for a binary or very large file.</summary>
    public string? ReadTextIfText(string relative)
    {
        var info = new FileInfo(Full(relative));
        if (info.Length > 2_000_000) return null;
        var bytes = File.ReadAllBytes(info.FullName);
        return Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 4096)) >= 0 ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    string Full(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    string Relative(string full) => Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Names of the nodes of the tree, from DMP/tree.txt (chapter 02).</summary>
    public IReadOnlyList<string> TreeNames()
    {
        var text = ReadText("DMP/tree.txt");
        return text is null
            ? []
            : text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
    }

    [GeneratedRegex(@"^(cm|CM)\.", RegexOptions.None)]
    private static partial Regex ProductPrefix();

    /// <summary>True for a top-level folder that claims a place in the tree by its name.</summary>
    public static bool LooksLikeProduct(string folderName) => ProductPrefix().IsMatch(folderName);
}

/// <summary>The base of a change exists but git could not read a file from it.</summary>
public sealed class BaseUnreadable(string message) : Exception(message);
