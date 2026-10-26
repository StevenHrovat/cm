using Im;
using Im.Language;
using Xunit;

namespace Im.Tests.Language;

/// <summary>The sentence on the wire (docs/language.md; proof/Im/Wire.lean): strict, canonical, well formed or refused.</summary>
[Trait("Category", "im")]
public sealed class ImSentenceTests
{
    static readonly ImKind SetupWrite = new(ImLayer.Setup, ImVerb.Write);
    static readonly ImKind FileRead = new(ImLayer.File, ImVerb.Read);

    [Fact]
    public void A_sentence_round_trips()
    {
        var sentence = new ImSentence(SetupWrite, "toolchain/lean", ImMode.Apply, ImExpect.Absent);
        var bytes = ImSentenceCodec.Encode(sentence);
        Assert.Equal(sentence, ImSentenceCodec.Parse(SetupWrite, bytes));
        Assert.Equal([0x49, 0x4d, 0x53, 0x01, 0x00, 0x0e], bytes[..6]);
    }

    [Fact]
    public void The_kind_names_the_permission()
    {
        Assert.Equal("setup.write", SetupWrite.Wire);
        Assert.Equal("setup.write", SetupWrite.Permission);
        Assert.Equal(SetupWrite, ImLanguage.KindOf("setup.write"));
        Assert.Null(ImLanguage.KindOf("setup.delete"));
        Assert.Equal(12, ImLanguage.Kinds.Count);
        Assert.Equal(ImLanguage.Kinds.Count, ImLanguage.Kinds.Select(k => k.Permission).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ImLanguage.Kinds, k => Assert.True(ImText.IsValid(k.Wire, ImAlphabet.Name, ImLimits.NameMax)));
    }

    [Fact]
    public void A_write_says_what_it_expects_and_a_read_does_not()
    {
        Assert.False(new ImSentence(SetupWrite, "a", ImMode.Dry, ImExpect.Any).IsWellFormed);
        Assert.True(new ImSentence(SetupWrite, "a", ImMode.Dry, ImExpect.Present).IsWellFormed);
        Assert.False(new ImSentence(FileRead, "a", ImMode.Dry, ImExpect.Present).IsWellFormed);
        Assert.True(new ImSentence(FileRead, "a", ImMode.Dry, ImExpect.Any).IsWellFormed);
        Assert.Throws<ArgumentException>(() => ImSentenceCodec.Encode(new ImSentence(SetupWrite, "a", ImMode.Dry, ImExpect.Any)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Toolchain")]
    [InlineData("a b")]
    [InlineData("\u00e9")]
    public void An_illegal_path_is_refused(string path)
    {
        Assert.False(new ImSentence(FileRead, path, ImMode.Dry, ImExpect.Any).IsWellFormed);
        Assert.Throws<ArgumentException>(() => ImSentenceCodec.Encode(new ImSentence(FileRead, path, ImMode.Dry, ImExpect.Any)));
    }

    [Fact]
    public void A_path_at_the_limit_round_trips_and_one_over_is_refused()
    {
        var atLimit = new string('a', ImLanguage.PathMax);
        Assert.Equal(atLimit, ImSentenceCodec.Parse(FileRead, ImSentenceCodec.Encode(new ImSentence(FileRead, atLimit, ImMode.Apply, ImExpect.Any))).Path);
        Assert.False(new ImSentence(FileRead, atLimit + "a", ImMode.Apply, ImExpect.Any).IsWellFormed);
    }

    [Fact]
    public void Trailing_bytes_a_wrong_version_an_unknown_byte_and_an_ill_formed_payload_are_malformed()
    {
        var good = ImSentenceCodec.Encode(new ImSentence(FileRead, "a", ImMode.Dry, ImExpect.Any));
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImSentenceCodec.Parse(FileRead, good.Append((byte)0).ToArray())).Error);
        var wrongVersion = good.ToArray(); wrongVersion[3] = 0;
        Assert.Equal(ImFormatError.UnknownVersion, Assert.Throws<ImFormatException>(() => ImSentenceCodec.Parse(FileRead, wrongVersion)).Error);
        var badMode = good.ToArray(); badMode[^2] = 7;
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImSentenceCodec.Parse(FileRead, badMode)).Error);
        var readWithExpect = good.ToArray(); readWithExpect[^1] = (byte)ImExpect.Present;
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImSentenceCodec.Parse(FileRead, readWithExpect)).Error);
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImSentenceCodec.Parse(FileRead, good[..^1])).Error);
        Assert.Equal(ImFormatError.Malformed, Assert.Throws<ImFormatException>(() => ImSentenceCodec.Parse(FileRead, ReadOnlyMemory<byte>.Empty)).Error);
    }
}
