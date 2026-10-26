using System.Text.RegularExpressions;
using cm.os.MonthEnd;
using CM.Pictor.Professional;
using CM.Pictor.Windows;
using Xunit;

namespace CM.Pictor.Tests;

public class MonthEndWindowTests
{
    static readonly DateTimeOffset At = new(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);

    static MonthEndBoard Sample() => new(
        "2026-10", At, 2, true,
        Enum.GetValues<SourceState>().Select((s, i) => new SourceTile(
            i + 1, $"Portal {i + 1}", s, 2, s == SourceState.Arrived ? 2 : 1, s == SourceState.Missing ? ["Holdings <per> fund"] : [],
            [new ExpectedFileView("Holdings", "portal/holdings.csv", 2048, At, new DateOnly(2026, 10, 31), s)])).ToList(),
        [new BoardFinding("abc", FindingKind.SpreadsheetErrorValue, 0, "delivery/fee_check.xlsx", "Sheet!D4", "3 cells show #VALUE!", false)],
        Enum.GetValues<BatonState>().Select((s, i) => new BatonStepView(i + 1, $"Step {i + 1}", "accounting lead", s, s == BatonState.DoneTicked ? "billing lead" : null, s == BatonState.DoneTicked ? At : null)).ToList());

    [Fact]
    public void No_token_of_the_professional_mode_is_orange()
    {
        foreach (var (name, hex) in ProfessionalTokens.Dark)
            Assert.False(HueFence.IsOrange(hex), $"token {name} ({hex}) is in the orange band");
    }

    [Theory]
    [InlineData("#ff8800", true)]
    [InlineData("#e8a33d", true)]
    [InlineData("#c46a1a", true)]
    [InlineData("#e5524a", false)]
    [InlineData("#4fb47a", false)]
    [InlineData("#7a746c", false)]
    public void The_fence_catches_orange_and_amber_and_lets_the_state_colours_pass(string hex, bool orange)
    {
        Assert.Equal(orange, HueFence.IsOrange(hex));
    }

    [Fact]
    public void The_rendered_page_uses_only_colours_from_the_tokens_and_none_of_them_orange()
    {
        var html = MonthEndWindow.Render(Sample());

        var literals = Regex.Matches(html, @"#[0-9a-fA-F]{6}\b|\b(?:rgb|hsl)a?\(", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Value.ToLowerInvariant()).Distinct();
        var tokens = ProfessionalTokens.Dark.Values.Select(v => v.ToLowerInvariant()).ToHashSet();
        foreach (var literal in literals)
        {
            Assert.StartsWith("#", literal);
            Assert.Contains(literal, tokens);
        }
        Assert.DoesNotMatch(@"\b(orange|amber|darkorange|coral|gold)\b", html.ToLowerInvariant());
    }

    [Fact]
    public void Every_state_is_shown_as_a_symbol_and_a_word()
    {
        foreach (var state in Enum.GetValues<SourceState>())
        {
            var (symbol, word, _) = MonthEndWindow.Describe(state);
            Assert.False(string.IsNullOrWhiteSpace(symbol));
            Assert.Contains($"<b>{symbol}</b> {word}", MonthEndWindow.Render(Sample()));
        }
    }

    [Fact]
    public void The_page_has_the_three_bands_and_one_tile_per_source()
    {
        var html = MonthEndWindow.Render(Sample());

        Assert.Contains("<h2>Sources</h2>", html);
        Assert.Contains("<h2>Findings", html);
        Assert.Contains("<h2>Baton passes</h2>", html);
        Assert.Equal(Enum.GetValues<SourceState>().Length, Regex.Matches(html, "<details class=\"tile\"").Count);
        Assert.Contains("Done · ticked · billing lead", html);
    }

    [Fact]
    public void Text_from_the_board_is_encoded()
    {
        Assert.Contains("Holdings &lt;per&gt; fund", MonthEndWindow.Render(Sample()));
    }

    [Fact]
    public void The_page_is_self_contained()
    {
        var html = MonthEndWindow.Render(Sample());

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void The_refresh_can_be_turned_off()
    {
        Assert.Contains("http-equiv=\"refresh\"", MonthEndWindow.Render(Sample()));
        Assert.DoesNotContain("http-equiv=\"refresh\"", MonthEndWindow.Render(Sample(), 0));
    }

    [Fact]
    public void Stale_is_not_violet_because_violet_is_the_dev_channels_colour()
    {
        var (hue, _) = HueFence.Hsl(ProfessionalTokens.Dark["state-stale"]);
        Assert.False(hue is >= 250 and <= 300, $"state-stale hue {hue:0} is in the violet band");
    }

    [Fact]
    public void Tick_buttons_are_plain_forms_for_open_and_ticked_passes_and_never_for_a_pass_the_board_saw()
    {
        var html = MonthEndWindow.Render(Sample(), 60, new TickControls("/tick", "/untick", "<input type=\"hidden\" name=\"t\" value=\"x\">"));

        Assert.Single(Regex.Matches(html, "action=\"/tick\""));
        Assert.Single(Regex.Matches(html, "action=\"/untick\""));
        Assert.Equal(2, Regex.Matches(html, "name=\"t\" value=\"x\"").Count);
        Assert.Contains("name=\"period\" value=\"2026-10\"", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<form", MonthEndWindow.Render(Sample()));
    }
}
