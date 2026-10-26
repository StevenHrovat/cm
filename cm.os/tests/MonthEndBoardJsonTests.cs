using cm.os.MonthEnd;
using Xunit;

namespace cm.os.Tests;

public class MonthEndBoardJsonTests
{
    [Fact]
    public void A_board_survives_the_round_trip_with_enums_as_words()
    {
        var board = new MonthEndBoard(
            "2026-10", new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero), 3, true,
            [new SourceTile(1, "Portal 1", SourceState.Partly, 2, 1, ["price series"], [])],
            [new BoardFinding("f1", FindingKind.SpreadsheetErrorValue, 1, "a.xlsx", "Sheet1!C4", "one cell shows an error", false)],
            [new BatonStepView(1, "Downloads", "accounting lead", BatonState.DoneSeen, null, null)]);

        var json = MonthEndBoardJson.Write(board);

        Assert.Contains("\"partly\"", json);
        Assert.Contains("\"spreadsheetErrorValue\"", json);
        var back = MonthEndBoardJson.Read(json);
        Assert.Equal(board.Period, back.Period);
        Assert.Equal(SourceState.Partly, back.Tiles[0].State);
        Assert.Equal(["price series"], back.Tiles[0].Missing);
        Assert.Equal(FindingKind.SpreadsheetErrorValue, back.Findings[0].Kind);
        Assert.Equal(BatonState.DoneSeen, back.Baton[0].State);
    }
}
