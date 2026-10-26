using System.Text;
using System.Text.RegularExpressions;

namespace Cm.Dmp;

// A reader for the block-style YAML that GitHub workflows are written in, used by DMP-020. It is deliberately small and fails closed:
// anything it does not understand (anchors, aliases, merge keys, flow mappings other than {}, tabs) throws YamlUnsupported, and the
// rule turns that into a finding, so a workflow cannot hide a key from the check by writing it in a form the reader skips.
// No package: the gate does not take a third-party dependency (DMP chapter 10).
public abstract record YNode;
public sealed record YScalar(string Value) : YNode;
public sealed record YSeq(List<YNode> Items) : YNode;
public sealed record YMap(List<KeyValuePair<string, YNode>> Entries) : YNode
{
    public YNode? this[string key] => Entries.FirstOrDefault(e => e.Key == key).Value;
    public IEnumerable<string> Keys => Entries.Select(e => e.Key);
}

public sealed class YamlUnsupported(int line, string reason) : Exception($"line {line}: {reason}")
{
    public int Line { get; } = line;
}

public static partial class WorkflowYaml
{
    [GeneratedRegex(@"^(?<key>""[^""]*""|'[^']*'|[A-Za-z0-9_.\-/]+)\s*:(\s+(?<rest>.*))?$")]
    private static partial Regex KeyLine();

    sealed record Line(int Number, int Indent, string Text, string Raw);

    public static YNode Parse(string text)
    {
        var raw = text.Replace("\r\n", "\n").Split('\n');
        var lines = new List<Line>();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i].Contains('\t')) throw new YamlUnsupported(i + 1, "a tab character");
            lines.Add(new(i + 1, raw[i].Length - raw[i].TrimStart(' ').Length, StripComment(raw[i]).Trim(), raw[i]));
        }
        var pos = 0;
        Skip(lines, ref pos);
        if (pos >= lines.Count) return new YMap([]);
        var node = ParseBlock(lines, ref pos, lines[pos].Indent);
        Skip(lines, ref pos);
        if (pos < lines.Count) throw new YamlUnsupported(lines[pos].Number, "content outside the top-level mapping");
        return node;
    }

    static void Skip(List<Line> lines, ref int pos)
    {
        while (pos < lines.Count && lines[pos].Text.Length == 0) pos++;
    }

    static YNode ParseBlock(List<Line> lines, ref int pos, int indent) =>
        IsDash(lines[pos].Text) ? ParseSeq(lines, ref pos, indent) : ParseMap(lines, ref pos, indent, null);

    static bool IsDash(string text) => text == "-" || text.StartsWith("- ", StringComparison.Ordinal);

    static YSeq ParseSeq(List<Line> lines, ref int pos, int indent)
    {
        var items = new List<YNode>();
        while (true)
        {
            Skip(lines, ref pos);
            if (pos >= lines.Count || lines[pos].Indent != indent || !IsDash(lines[pos].Text)) break;
            var line = lines[pos];
            var rest = line.Text.Length > 1 ? line.Text[2..].TrimStart() : "";
            if (rest.Length == 0)
            {
                pos++;
                Skip(lines, ref pos);
                if (pos >= lines.Count || lines[pos].Indent <= indent) { items.Add(new YScalar("")); continue; }
                items.Add(ParseBlock(lines, ref pos, lines[pos].Indent));
            }
            else if (KeyLine().IsMatch(rest))
            {
                // "- key: value" opens a mapping whose keys sit at the column of "key".
                var column = line.Raw.IndexOf(rest, line.Indent + 1, StringComparison.Ordinal);
                items.Add(ParseMap(lines, ref pos, column, rest));
            }
            else
            {
                items.Add(Scalar(rest, line.Number));
                pos++;
            }
        }
        return new YSeq(items);
    }

    static YMap ParseMap(List<Line> lines, ref int pos, int indent, string? firstText)
    {
        var entries = new List<KeyValuePair<string, YNode>>();
        var first = firstText is not null;
        while (true)
        {
            string text;
            if (first) text = firstText!;
            else
            {
                Skip(lines, ref pos);
                if (pos >= lines.Count || lines[pos].Indent != indent || IsDash(lines[pos].Text)) break;
                text = lines[pos].Text;
            }
            first = false;
            var line = lines[pos];
            var match = KeyLine().Match(text);
            if (!match.Success) throw new YamlUnsupported(line.Number, $"expected 'key: value', found '{text}'");
            var key = match.Groups["key"].Value.Trim('"', '\'');
            if (key == "<<") throw new YamlUnsupported(line.Number, "a merge key");
            if (entries.Any(e => e.Key == key)) throw new YamlUnsupported(line.Number, $"the key '{key}' twice");
            var rest = match.Groups["rest"].Value.Trim();
            pos++;
            YNode value;
            if (rest.Length == 0)
            {
                Skip(lines, ref pos);
                if (pos < lines.Count && lines[pos].Indent > indent) value = ParseBlock(lines, ref pos, lines[pos].Indent);
                else if (pos < lines.Count && lines[pos].Indent == indent && IsDash(lines[pos].Text)) value = ParseSeq(lines, ref pos, indent);
                else value = new YScalar("");
            }
            else if (rest[0] is '|' or '>') value = BlockScalar(lines, ref pos, indent, rest, line.Number);
            else
            {
                value = Scalar(rest, line.Number);
                // A plain scalar may continue on more-indented lines; fold them in.
                if (value is YScalar s)
                {
                    var folded = new StringBuilder(s.Value);
                    while (pos < lines.Count && lines[pos].Text.Length > 0 && lines[pos].Indent > indent) folded.Append(' ').Append(lines[pos++].Text);
                    value = new YScalar(folded.ToString());
                }
            }
            entries.Add(new(key, value));
        }
        return new YMap(entries);
    }

    static YScalar BlockScalar(List<Line> lines, ref int pos, int indent, string header, int number)
    {
        if (header.TrimEnd('-', '+', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length != 1) throw new YamlUnsupported(number, $"block scalar header '{header}'");
        var body = new List<string>();
        int? blockIndent = null;
        while (pos < lines.Count)
        {
            var line = lines[pos];
            var blank = line.Raw.Trim().Length == 0;
            if (!blank && line.Indent <= indent) break;
            if (!blank) blockIndent ??= line.Indent;
            body.Add(blank ? "" : line.Raw[Math.Min(blockIndent ?? 0, line.Raw.Length)..]);
            pos++;
        }
        while (body.Count > 0 && body[^1].Length == 0) body.RemoveAt(body.Count - 1);
        return new YScalar(header[0] == '>' ? string.Join(' ', body) : string.Join('\n', body));
    }

    static YNode Scalar(string rest, int number)
    {
        if (rest[0] is '&' or '*' or '!') throw new YamlUnsupported(number, "an anchor, alias or tag");
        if (rest == "{}") return new YMap([]);
        if (rest == "[]") return new YSeq([]);
        if (rest[0] == '{') throw new YamlUnsupported(number, "a flow mapping; write the keys on their own lines");
        if (rest[0] == '[')
        {
            if (!rest.EndsWith(']') || rest[1..^1].IndexOfAny(['[', ']', '{', '}']) >= 0) throw new YamlUnsupported(number, "a nested flow sequence");
            return new YSeq(rest[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(v => (YNode)new YScalar(Unquote(v))).ToList());
        }
        return new YScalar(Unquote(rest));
    }

    static string Unquote(string v) =>
        v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')) ? v[1..^1] : v;

    // Drops a trailing "# comment": a '#' at the start or after a space, outside quotes.
    static string StripComment(string line)
    {
        char? quote = null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote is null && c is '"' or '\'') quote = c;
            else if (quote == c) quote = null;
            else if (quote is null && c == '#' && (i == 0 || line[i - 1] == ' ')) return line[..i];
        }
        return line;
    }
}
