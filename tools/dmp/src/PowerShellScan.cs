using System.Text;

namespace Cm.Dmp;

/// <summary>
/// A small scanner for PowerShell source, enough for DMP-011 (DMP-011): it removes comments (line, block, and the help block) while keeping strings and
/// here-strings intact and keeping every line break, so a line number in the result is the line number in the file.
/// </summary>
internal static class PowerShellScan
{
    public static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '<' && next == '#')
            {
                var end = text.IndexOf("#>", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 2;
                for (var k = i; k < stop; k++)
                {
                    if (text[k] == '\n')
                    {
                        sb.Append('\n');
                    }
                }

                i = stop;
                continue;
            }

            if (c == '#')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '@' && (next == '\'' || next == '"') && RestOfLineIsBlank(text, i + 2))
            {
                var closer = "\n" + next + "@";
                var end = text.IndexOf(closer, i, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + closer.Length;
                sb.Append(text, i, stop - i);
                i = stop;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                var start = i;
                i = SkipString(text, i);
                sb.Append(text, start, i - start);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Comment-free code with the inside of every string and here-string blanked (line breaks kept), so a command name inside a message does not count as a command.</summary>
    public static string BlankStrings(string code)
    {
        var sb = new StringBuilder(code.Length);
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (c == '@' && (next == '\'' || next == '"') && RestOfLineIsBlank(code, i + 2))
            {
                var closer = "\n" + next + "@";
                var end = code.IndexOf(closer, i, StringComparison.Ordinal);
                var stop = end < 0 ? code.Length : end + closer.Length;
                for (var k = i; k < stop; k++)
                {
                    sb.Append(code[k] == '\n' ? '\n' : ' ');
                }

                i = stop;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                var start = i;
                i = SkipString(code, i);
                for (var k = start; k < i; k++)
                {
                    sb.Append(code[k] == '\n' ? '\n' : ' ');
                }

                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>For the position <paramref name="index"/>: the header text (from the previous brace to the opening brace) of every block that encloses it, innermost first. Run it on <see cref="BlankStrings"/> output.</summary>
    public static List<string> EnclosingHeaders(string code, int index)
    {
        var headers = new List<string>();
        var depth = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            if (code[i] == '}')
            {
                depth++;
            }
            else if (code[i] == '{')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                var from = i;
                while (from > 0 && code[from - 1] != '{' && code[from - 1] != '}')
                {
                    from--;
                }

                headers.Add(code[from..i]);
            }
        }

        return headers;
    }

    /// <summary>The index just after the string that starts at <paramref name="start"/> ('...' or "..."). A "..." string may hold $( ... ) with strings of its own: <c>"a $($x.Replace("'", "''")) b"</c> is one string.</summary>
    private static int SkipString(string text, int start)
    {
        var quote = text[start];
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '`' && quote == '"')
            {
                i += 2;
                continue;
            }

            if (c == '$' && quote == '"' && i + 1 < text.Length && text[i + 1] == '(')
            {
                i = SkipSubexpression(text, i);
                continue;
            }

            if (c == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return text.Length;
    }

    /// <summary>The index just after the balanced <c>$( ... )</c> that starts at <paramref name="start"/>, strings inside it respected.</summary>
    private static int SkipSubexpression(string text, int start)
    {
        var depth = 0;
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\'' || c == '"')
            {
                i = SkipString(text, i);
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return i + 1;
            }

            i++;
        }

        return text.Length;
    }

    private static bool RestOfLineIsBlank(string text, int from)
    {
        for (var i = from; i < text.Length && text[i] != '\n'; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes the first balanced <c>param( ... )</c> (strings respected) from comment-free code.</summary>
    public static string RemoveParamBlock(string code)
    {
        var m = System.Text.RegularExpressions.Regex.Match(code, @"(?im)^\s*param\s*\(");
        if (!m.Success)
        {
            return code;
        }

        var depth = 0;
        for (var i = m.Index + m.Length - 1; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '\'' || c == '"')
            {
                for (i++; i < code.Length && code[i] != c; i++)
                {
                    if (c == '"' && code[i] == '`')
                    {
                        i++;
                    }
                }

                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return code[..m.Index] + code[(i + 1)..];
            }
        }

        return code;
    }
}
