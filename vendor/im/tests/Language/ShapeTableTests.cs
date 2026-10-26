using Im.Language;
using Xunit;

namespace Im.Tests.Language;

/// <summary>
/// The C# against the proof, exhaustively (DMP chapter 06, "IM: checked, exhaustively"): proof/table/shapes.tsv is what the Lean proof
/// prints for every well-formed shape (kind, mode, expect) with the sample path "a"; proof/tools/check.sh fails when the committed table is
/// not what the proof prints, and this test fails when the C# does not reproduce every row or has a shape the proof does not.
/// </summary>
[Trait("Category", "im")]
public sealed class ShapeTableTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Im.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Im.slnx not found above the test directory");
    }

    static IReadOnlyList<(string Kind, string Permission, string Mode, string Expect, string Hex)> Table() =>
        File.ReadAllLines(Path.Combine(RepoRoot(), "proof", "table", "shapes.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t'))
            .Select(c => (c[0], c[1], c[2], c[3], c[4]))
            .ToArray();

    static IEnumerable<ImSentence> AllWellFormedSamples() =>
        from kind in ImLanguage.Kinds
        from mode in Enum.GetValues<ImMode>()
        from expect in Enum.GetValues<ImExpect>()
        let s = new ImSentence(kind, "a", mode, expect)
        where s.IsWellFormed
        select s;

    [Fact]
    public void Every_row_of_the_proofs_table_is_reproduced_by_the_codec()
    {
        var rows = Table();
        Assert.Equal(32, rows.Count);
        foreach (var (kindName, permission, modeName, expectName, hex) in rows)
        {
            var kind = ImLanguage.KindOf(kindName);
            Assert.NotNull(kind);
            Assert.Equal(permission, kind.Value.Permission);
            var sentence = new ImSentence(kind.Value, "a", Enum.Parse<ImMode>(modeName, ignoreCase: true), Enum.Parse<ImExpect>(expectName, ignoreCase: true));
            Assert.True(sentence.IsWellFormed, kindName);
            Assert.Equal(hex, Convert.ToHexStringLower(ImSentenceCodec.Encode(sentence)));
            Assert.Equal(sentence, ImSentenceCodec.Parse(kind.Value, Convert.FromHexString(hex)));
        }
    }

    [Fact]
    public void The_codec_has_no_shape_the_proof_does_not()
    {
        var rows = Table().Select(r => (r.Kind, r.Mode, r.Expect)).ToHashSet();
        var samples = AllWellFormedSamples().ToArray();
        Assert.Equal(rows.Count, samples.Length);
        foreach (var s in samples) Assert.Contains((s.Kind.Wire, ImLanguageNames.Lower(s.Mode), ImLanguageNames.Lower(s.Expect)), rows);
    }

    static class ImLanguageNames
    {
        public static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    }
}
