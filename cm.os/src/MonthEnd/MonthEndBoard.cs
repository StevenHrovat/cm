using System.Text.Json;
using System.Text.Json.Serialization;

namespace cm.os.MonthEnd;

// The month-end board: the one read model that Sagittarius produces and a Pictor module shows (docs/01 of CM.Sagittarius,
// constitution C4: one spelling now, an IM message later). It lives here because a constellation never references another.
// The board carries file states and counts only. It never carries an amount, a holding or a file's contents.

/// <summary>The state of a source tile or of one expected file. A state is always shown as a word and a symbol, never as a colour alone.</summary>
public enum SourceState { NotYetDue, Missing, Partly, Arrived, Stale, Failing }

/// <summary>The kinds of finding of the first version (the #VALUE! class).</summary>
public enum FindingKind { SpreadsheetErrorValue, HeaderDrift, WrongMonth, MissingFund, DuplicateRows, OutputOlderThanInput, Unreadable }

/// <summary>Open, done because the board saw the evidence, or done because a lead ticked it.</summary>
public enum BatonState { Open, DoneSeen, DoneTicked }

/// <summary>One expected file of a source and what was observed for it. <c>Observed</c> is a path inside the watched root.</summary>
public sealed record ExpectedFileView(
    string Label, string? Observed, long? SizeBytes, DateTimeOffset? LastWriteUtc, DateOnly? AsOf, SourceState State);

/// <summary>One of the eight source tiles. <c>Role</c> is a role such as "Portal 3", never a company name.</summary>
public sealed record SourceTile(
    int Number, string Role, SourceState State, int ExpectedCount, int ArrivedCount,
    IReadOnlyList<string> Missing, IReadOnlyList<ExpectedFileView> Files);

/// <summary>A finding is information; the board fixes nothing. <c>Location</c> is a sheet and cell or a row, never a value.</summary>
public sealed record BoardFinding(
    string Id, FindingKind Kind, int SourceNumber, string File, string Location, string Sentence, bool Seen);

/// <summary>One baton pass of the work instruction with its owner role and state.</summary>
public sealed record BatonStepView(
    int Order, string Title, string OwnerRole, BatonState State, string? By, DateTimeOffset? At);

/// <summary>The whole board for one month being closed.</summary>
public sealed record MonthEndBoard(
    string Period, DateTimeOffset GeneratedAtUtc, int CloseBusinessDay, bool Synthetic,
    IReadOnlyList<SourceTile> Tiles, IReadOnlyList<BoardFinding> Findings, IReadOnlyList<BatonStepView> Baton);

/// <summary>The JSON spelling of the board. Enums are words; the format is the contract between the data side and the window.</summary>
public static class MonthEndBoardJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(MonthEndBoard board) => JsonSerializer.Serialize(board, Options);

    public static MonthEndBoard Read(string json) =>
        JsonSerializer.Deserialize<MonthEndBoard>(json, Options) ?? throw new JsonException("The board is empty.");
}
