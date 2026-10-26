using System.Globalization;
using System.Net;
using System.Text;
using cm.os.MonthEnd;
using CM.Pictor.Professional;

namespace CM.Pictor.Windows;

/// <summary>Where the tick buttons post to. <c>HiddenFieldsHtml</c> is markup the host needs in every form (an anti-forgery token); the window never inspects it.</summary>
public sealed record TickControls(string TickAction, string UntickAction, string HiddenFieldsHtml = "");

/// <summary>
/// The month-end cockpit as a Pictor professional module (layout profile as for every module window): one window, three
/// bands, 1920 × 1080, dark greys, never orange. The shell shows it in a web view, as every module window does, and the
/// same page opens in any browser. It only reads the board: it changes nothing in the department's work and shows no amount.
/// </summary>
public static class MonthEndWindow
{
    /// <summary>A state is always a symbol and a word, never a colour alone.</summary>
    public static (string Symbol, string Word, string Token) Describe(SourceState state) => state switch
    {
        SourceState.Arrived => ("✓", "Arrived", "state-arrived"),
        SourceState.Partly => ("◐", "Partly", "state-partly"),
        SourceState.Missing => ("○", "Missing", "state-missing"),
        SourceState.NotYetDue => ("·", "Not yet due", "state-not-yet-due"),
        SourceState.Stale => ("◆", "Stale", "state-stale"),
        SourceState.Failing => ("✖", "Failing", "state-failing"),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    static (string Symbol, string Word, string Token) Describe(BatonState state) => state switch
    {
        BatonState.DoneSeen => ("✓", "Done · seen", "state-arrived"),
        BatonState.DoneTicked => ("✓", "Done · ticked", "state-partly"),
        _ => ("○", "Open", "state-not-yet-due"),
    };

    static string FindingKindWord(FindingKind kind) => kind switch
    {
        FindingKind.SpreadsheetErrorValue => "Error value",
        FindingKind.HeaderDrift => "Header drift",
        FindingKind.WrongMonth => "Wrong month",
        FindingKind.MissingFund => "Missing fund",
        FindingKind.DuplicateRows => "Duplicate rows",
        FindingKind.OutputOlderThanInput => "Output older than input",
        FindingKind.Unreadable => "Unreadable file",
        _ => kind.ToString(),
    };

    static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    static string Stamp(DateTimeOffset? at) => at is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "–";

    static string Size(long? bytes) => bytes is { } b ? (b < 1024 ? $"{b.ToString(CultureInfo.InvariantCulture)} B" : $"{(b / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB") : "–";

    static string Chip(SourceState state)
    {
        var (symbol, word, token) = Describe(state);
        return $"<span class=\"chip\" style=\"--c:var(--{token})\"><b>{symbol}</b> {word}</span>";
    }

    /// <summary>
    /// Renders the board as one self-contained page. <paramref name="refreshSeconds"/> 0 turns the reload off. With <paramref name="tick"/> the
    /// baton passes get a plain form button (no script): the page stays a read model, the host decides who may post and what happens.
    /// </summary>
    public static string Render(MonthEndBoard board, int refreshSeconds = 60, TickControls? tick = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Month-end cockpit</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        if (refreshSeconds > 0) sb.Append("<meta http-equiv=\"refresh\" content=\"").Append(refreshSeconds.ToString(CultureInfo.InvariantCulture)).Append("\">");
        sb.Append("<style>").Append(Css()).Append("</style></head><body>");

        // Header
        sb.Append("<header><div><h1>Month-end cockpit</h1><div class=\"sub\">Closing ").Append(E(board.Period))
          .Append(" · business day ").Append(board.CloseBusinessDay.ToString(CultureInfo.InvariantCulture))
          .Append(" of the close · read only, nothing here changes your work</div></div><div class=\"meta\">");
        if (board.Synthetic) sb.Append("<span class=\"badge\">Synthetic data</span>");
        sb.Append("<span>Folder read ").Append(Stamp(board.GeneratedAtUtc)).Append("</span></div></header>");

        // Band 1
        sb.Append("<section><h2>Sources</h2><div class=\"tiles\">");
        foreach (var tile in board.Tiles)
        {
            var (_, _, token) = Describe(tile.State);
            sb.Append("<details class=\"tile\" style=\"--c:var(--").Append(token).Append(")\"><summary><div class=\"tile-head\"><span class=\"no\">")
              .Append(tile.Number.ToString(CultureInfo.InvariantCulture)).Append("</span><span class=\"role\">").Append(E(tile.Role)).Append("</span></div>")
              .Append(Chip(tile.State)).Append("<div class=\"count\">").Append(tile.ArrivedCount.ToString(CultureInfo.InvariantCulture))
              .Append(" of ").Append(tile.ExpectedCount.ToString(CultureInfo.InvariantCulture)).Append(" files</div>");
            if (tile.Missing.Count > 0)
            {
                sb.Append("<ul class=\"missing\">");
                foreach (var m in tile.Missing.Take(3)) sb.Append("<li>").Append(E(m)).Append("</li>");
                if (tile.Missing.Count > 3) sb.Append("<li>+ ").Append((tile.Missing.Count - 3).ToString(CultureInfo.InvariantCulture)).Append(" more</li>");
                sb.Append("</ul>");
            }
            sb.Append("</summary><table class=\"files\"><tr><th>Expected</th><th>Observed</th><th>Size</th><th>Last written</th><th>As of</th><th>State</th></tr>");
            foreach (var f in tile.Files)
                sb.Append("<tr><td>").Append(E(f.Label)).Append("</td><td>").Append(E(f.Observed ?? "–")).Append("</td><td>").Append(Size(f.SizeBytes))
                  .Append("</td><td>").Append(Stamp(f.LastWriteUtc)).Append("</td><td>").Append(f.AsOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "–")
                  .Append("</td><td>").Append(Chip(f.State)).Append("</td></tr>");
            sb.Append("</table></details>");
        }
        sb.Append("</div></section>");

        // Band 2
        sb.Append("<section><h2>Findings <span class=\"n\">").Append(board.Findings.Count(f => !f.Seen).ToString(CultureInfo.InvariantCulture)).Append(" open</span></h2>");
        if (board.Findings.Count == 0) sb.Append("<p class=\"none\">No findings for this month.</p>");
        else
        {
            sb.Append("<table class=\"findings\"><tr><th>Kind</th><th>File</th><th>Where</th><th>What</th><th></th></tr>");
            foreach (var f in board.Findings)
                sb.Append("<tr class=\"").Append(f.Seen ? "seen" : "").Append("\"><td><span class=\"kind\">").Append(FindingKindWord(f.Kind)).Append("</span></td><td>")
                  .Append(E(f.File)).Append("</td><td>").Append(E(f.Location)).Append("</td><td>").Append(E(f.Sentence)).Append("</td><td>")
                  .Append(f.Seen ? "Seen" : "").Append("</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("</section>");

        // Band 3
        sb.Append("<section><h2>Baton passes</h2><ol class=\"baton\">");
        foreach (var step in board.Baton)
        {
            var (symbol, word, token) = Describe(step.State);
            sb.Append("<li style=\"--c:var(--").Append(token).Append(")\"><span class=\"step\">").Append(step.Order.ToString(CultureInfo.InvariantCulture))
              .Append("</span><span class=\"title\">").Append(E(step.Title)).Append("</span><span class=\"owner\">").Append(E(step.OwnerRole)).Append("</span><span class=\"chip\"><b>")
              .Append(symbol).Append("</b> ").Append(word).Append(step.By is null ? "" : $" · {E(step.By)}, {Stamp(step.At)}").Append("</span>");
            if (tick is not null && step.State != BatonState.DoneSeen) sb.Append(TickForm(tick, board.Period, step));
            sb.Append("</li>");
        }
        sb.Append("</ol></section></body></html>");
        return sb.ToString();
    }

    static string TickForm(TickControls tick, string period, BatonStepView step)
    {
        var ticked = step.State == BatonState.DoneTicked;
        return $"<form class=\"tick\" method=\"post\" action=\"{E(ticked ? tick.UntickAction : tick.TickAction)}\">{tick.HiddenFieldsHtml}" +
               $"<input type=\"hidden\" name=\"period\" value=\"{E(period)}\"><input type=\"hidden\" name=\"order\" value=\"{step.Order.ToString(CultureInfo.InvariantCulture)}\">" +
               $"<button type=\"submit\">{(ticked ? "Untick" : "Tick")}</button></form>";
    }

    static string Css()
    {
        var sb = new StringBuilder(":root{color-scheme:dark;");
        foreach (var (name, value) in ProfessionalTokens.Dark) sb.Append("--").Append(name).Append(':').Append(value).Append(';');
        sb.Append('}');
        sb.Append("""
            *{box-sizing:border-box}
            body{margin:0;padding:16px 24px;background:var(--bg);color:var(--text);font:14px/1.35 "Segoe UI",system-ui,sans-serif;min-width:1280px}
            header{display:flex;justify-content:space-between;align-items:flex-end;border-bottom:1px solid var(--border);padding-bottom:10px}
            h1{margin:0;font-size:22px;font-weight:600;letter-spacing:.2px}
            h2{margin:18px 0 8px;font-size:13px;text-transform:uppercase;letter-spacing:1.2px;color:var(--text-muted);font-weight:600}
            h2 .n{margin-left:8px;color:var(--accent);letter-spacing:0;text-transform:none}
            .sub,.meta{color:var(--text-muted)}
            .meta{display:flex;gap:14px;align-items:center}
            .badge{border:1px solid var(--accent);color:var(--accent);border-radius:999px;padding:2px 10px;font-size:12px}
            .tiles{display:grid;grid-template-columns:repeat(4,1fr);gap:10px}
            .tile{background:var(--surface);border:1px solid var(--border);border-left:4px solid var(--c);border-radius:6px}
            .tile summary{list-style:none;cursor:pointer;padding:10px 12px;min-height:112px}
            .tile summary::-webkit-details-marker{display:none}
            .tile[open]{grid-column:1/-1;background:var(--surface-raised)}
            .tile-head{display:flex;align-items:center;gap:8px;margin-bottom:6px}
            .no{background:var(--surface-raised);border:1px solid var(--border);border-radius:4px;padding:0 7px;color:var(--accent);font-weight:600}
            .role{font-weight:600}
            .chip{display:inline-block;border:1px solid var(--c,var(--border));color:var(--c,var(--text));border-radius:999px;padding:1px 9px;font-size:12px;white-space:nowrap}
            .chip b{font-weight:700}
            .count{margin-top:6px;color:var(--text-muted)}
            .missing{margin:6px 0 0;padding-left:16px;color:var(--text-muted);font-size:12px}
            table{border-collapse:collapse;width:100%}
            th{text-align:left;color:var(--text-muted);font-weight:500;font-size:12px;padding:4px 8px;border-bottom:1px solid var(--border)}
            td{padding:5px 8px;border-bottom:1px solid var(--border);vertical-align:top}
            .files{margin:0 12px 12px;width:calc(100% - 24px)}
            .findings{background:var(--surface);border:1px solid var(--border);border-radius:6px}
            .findings tr.seen td{color:var(--text-muted)}
            .kind{color:var(--accent)}
            .none{color:var(--text-muted);margin:4px 0}
            .baton{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(2,1fr);gap:6px 10px}
            .baton li{display:grid;grid-template-columns:28px 1fr auto auto auto;gap:10px;align-items:center;background:var(--surface);border:1px solid var(--border);border-left:4px solid var(--c);border-radius:6px;padding:7px 10px}
            .tick{margin:0}
            .tick button{background:var(--surface-raised);color:var(--text);border:1px solid var(--border);border-radius:4px;padding:2px 12px;font:inherit;font-size:12px;cursor:pointer}
            .tick button:hover{border-color:var(--accent);color:var(--accent)}
            .step{color:var(--accent);font-weight:600}
            .owner{color:var(--text-muted);font-size:12px}
            .baton .chip{border-color:var(--c);color:var(--c)}
            """);
        return sb.ToString();
    }
}
