using System.Security.Cryptography;
using System.Text;

namespace Cm.Dmp;

// DMP-015: IM stays backwards compatible once it is locked (constitution C18; DMP/im-lock.txt). For every locked version:
// (a) the line names the owner's words and a corpus folder that exists and holds at least one file;
// (b) the corpus is unchanged: its SHA-256 equals the recorded one, so a locked message can never be edited to fit new code;
// (c) the replay test exists (cm.os/tests/Im/ImLockReplayTests.cs), which runs every corpus message through today's verifier with the
//     anchors and the clock it was captured with and expects it to be accepted with the same meaning.
// While nothing is locked the rule only checks the file's shape.
public static partial class Rules
{
    const string ImLock = "DMP/im-lock.txt";
    const string ImReplayTest = "cm.os/tests/Im/ImLockReplayTests.cs";

    public static string CorpusHash(RepoView repo, string folder)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in repo.Files(folder).Order(StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(file[(folder.TrimEnd('/').Length + 1)..] + "\n"));
            sha.AppendData(repo.ReadBytes(file));
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    static IEnumerable<Finding> ImLockRule(RepoView repo)
    {
        var text = repo.ReadText(ImLock);
        if (text is null) { yield return new("DMP-015", ImLock, "missing: the list of locked IM versions (empty until the first productive system speaks IM)"); yield break; }
        var locked = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        foreach (var l in locked)
        {
            if (l.Length != 4 || !int.TryParse(l[0], out _) || l[1].Trim().Length < 12) { yield return new("DMP-015", ImLock, $"'{string.Join(' ', l)}' is not version<TAB>owner's words<TAB>corpus folder<TAB>sha256"); continue; }
            if (!repo.Files(l[2]).Any()) { yield return new("DMP-015", ImLock, $"IM version {l[0]}: the corpus folder '{l[2]}' is missing or empty"); continue; }
            if (CorpusHash(repo, l[2]) != l[3].Trim().ToLowerInvariant())
                yield return new("DMP-015", l[2], $"the corpus of locked IM version {l[0]} changed; a locked version's messages never change (constitution C18)");
        }
        if (locked.Count > 0 && !repo.Exists(ImReplayTest))
            yield return new("DMP-015", ImReplayTest, "missing: with a locked IM version, this test must replay every locked corpus through today's verifier");
    }
}
