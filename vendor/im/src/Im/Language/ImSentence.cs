using System.Text;

namespace Im.Language;

/// <summary>The three verbs (docs/language.md). read: information flows from the noun to the part; write: from the part to the noun; execute: the noun changes state and only the record flows.</summary>
public enum ImVerb
{
    Read,
    Write,
    Execute,
}

/// <summary>The information layers a noun belongs to (DMP C12). The list grows by addition at a stable promotion (C4); <c>Setup</c> is the first: what a root repository needs to run itself.</summary>
public enum ImLayer
{
    Setup,
    Board,
    File,
    Record,
}

/// <summary>The mode adjective (DMP chapter 10): a dry run changes nothing and says what it would do; apply does it.</summary>
public enum ImMode
{
    Dry,
    Apply,
}

/// <summary>What a write expects to find (C15): absent for a create, present for an overwrite; <c>Any</c> is what a read or an execute says.</summary>
public enum ImExpect
{
    Any,
    Absent,
    Present,
}

/// <summary>A kind: a layer and a verb. Its wire name <c>&lt;layer&gt;.&lt;verb&gt;</c> is the envelope's kind and the permission the token needs (DMP chapter 04: exactly one per kind).</summary>
public readonly record struct ImKind(ImLayer Layer, ImVerb Verb)
{
    public string Wire => $"{ImLanguage.Wire(Layer)}.{ImLanguage.Wire(Verb)}";

    public string Permission => Wire;
}

/// <summary>
/// One sentence of IM: an act (DMP, the words). The kind names the verb and the layer, the path names the item inside the layer, the
/// adjectives qualify the act. A sentence is well formed when its path is legal and a write says what it expects (C15) while a read or an
/// execute says <see cref="ImExpect.Any"/>; <see cref="ImSentenceCodec"/> emits and accepts well-formed sentences only.
/// </summary>
public sealed record ImSentence(ImKind Kind, string Path, ImMode Mode, ImExpect Expect)
{
    public bool IsWellFormed => ImLanguage.IsPath(Path) && ImLanguage.ExpectFits(Kind.Verb, Expect);
}

/// <summary>The grammar as data: the wire spellings, the alphabets, the limits and the table of kinds. Checked against the proof's table (proof/table/shapes.tsv) by the tests.</summary>
public static class ImLanguage
{
    /// <summary>A path is 1 to 1024 bytes of <c>a-z 0-9 . - /</c>.</summary>
    public const int PathMax = 1024;

    public static string Wire(ImVerb verb) => verb switch
    {
        ImVerb.Read => "read",
        ImVerb.Write => "write",
        ImVerb.Execute => "execute",
        _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, "unknown verb"),
    };

    public static string Wire(ImLayer layer) => layer switch
    {
        ImLayer.Setup => "setup",
        ImLayer.Board => "board",
        ImLayer.File => "file",
        ImLayer.Record => "record",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "unknown layer"),
    };

    /// <summary>Every kind of the language, in the order of the proof (layers, then verbs).</summary>
    public static IReadOnlyList<ImKind> Kinds { get; } = Enum.GetValues<ImLayer>().SelectMany(l => Enum.GetValues<ImVerb>().Select(v => new ImKind(l, v))).ToArray();

    /// <summary>The kind for a wire name, or null when the name is not a kind (the verifier refuses it as unknown, DMP chapter 04).</summary>
    public static ImKind? KindOf(string wire)
    {
        foreach (var kind in Kinds) if (string.Equals(kind.Wire, wire, StringComparison.Ordinal)) return kind;
        return null;
    }

    public static bool IsPath(string path)
    {
        if (path.Length == 0 || path.Length > PathMax) return false;
        foreach (var c in path) if (!(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '/')) return false;
        return true;
    }

    public static bool ExpectFits(ImVerb verb, ImExpect expect) => verb == ImVerb.Write ? expect != ImExpect.Any : expect == ImExpect.Any;
}

/// <summary>
/// The sentence on the wire, the payload of an envelope whose kind is the sentence's kind: magic <c>"IMS"</c>, version 1, the path as
/// <c>bytes16</c>, the mode byte, the expect byte; nothing else, nothing trailing (I6; proof/Im/Wire.lean proves the round trip for every
/// well-formed sentence with any legal path).
/// </summary>
public static class ImSentenceCodec
{
    public const string Magic = "IMS";
    public const byte Version = 1;

    public static byte[] Encode(ImSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        if (!sentence.IsWellFormed) throw new ArgumentException("not a well-formed sentence: an illegal path, or a write without an expectation, or a read or execute with one", nameof(sentence));
        return new ImWriter()
            .Magic(Magic, Version)
            .Bytes16(Encoding.ASCII.GetBytes(sentence.Path), ImLanguage.PathMax)
            .U8((byte)sentence.Mode)
            .U8((byte)sentence.Expect)
            .ToArray();
    }

    /// <summary>Parses the payload of an envelope of the given kind strictly; anything else is <see cref="ImFormatException"/>.</summary>
    public static ImSentence Parse(ImKind kind, ReadOnlyMemory<byte> payload)
    {
        var reader = new ImReader(payload);
        reader.Magic(Magic, Version);
        var raw = reader.Bytes16(ImLanguage.PathMax);
        foreach (var b in raw) if (b > 0x7F) throw new ImFormatException(ImFormatError.Malformed);
        var path = Encoding.ASCII.GetString(raw);
        var mode = reader.U8();
        var expect = reader.U8();
        reader.End();
        if (!ImLanguage.IsPath(path) || mode > (byte)ImMode.Apply || expect > (byte)ImExpect.Present) throw new ImFormatException(ImFormatError.Malformed);
        var sentence = new ImSentence(kind, path, (ImMode)mode, (ImExpect)expect);
        if (!sentence.IsWellFormed) throw new ImFormatException(ImFormatError.Malformed);
        return sentence;
    }
}
