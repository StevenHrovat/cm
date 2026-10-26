using System.IO.Compression;
using System.Xml.Linq;

namespace cm.io.Spreadsheet;

public sealed record SheetInfo(string Name, bool Hidden, string PartPath);

/// <summary>One cell as the file stores it. Value is the cached value (what the user last saw), Formula the formula text if any.</summary>
public sealed record SheetCell(string Address, string? Value, string? Formula, string? SharedIndex, bool IsError);

/// <summary>
/// Read-only reader of .xlsx/.xlsm files without the spreadsheet application: zip plus XML. The file is opened for reading with
/// sharing allowed and is never written. Cached values are the last computed state, which is what a user saw (including #VALUE!).
/// </summary>
public sealed class XlsxWorkbook : IDisposable
{
    static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    readonly ZipArchive _zip;
    readonly Lazy<string[]> _shared;

    XlsxWorkbook(ZipArchive zip)
    {
        _zip = zip;
        _shared = new Lazy<string[]>(LoadSharedStrings);
        Sheets = LoadSheets();
    }

    public IReadOnlyList<SheetInfo> Sheets { get; }

    public static XlsxWorkbook Open(string path) =>
        new(new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), ZipArchiveMode.Read));

    public static XlsxWorkbook Open(Stream stream) => new(new ZipArchive(stream, ZipArchiveMode.Read));

    public void Dispose() => _zip.Dispose();

    /// <summary>Targets of external links (the raw strings; callers decide what to publish).</summary>
    public IReadOnlyList<string> ExternalLinkTargets() =>
        _zip.Entries.Where(e => e.FullName.StartsWith("xl/externalLinks/_rels/", StringComparison.Ordinal) && e.FullName.EndsWith(".rels", StringComparison.Ordinal))
            .SelectMany(e => Load(e.FullName).Descendants(PkgRel + "Relationship"))
            .Select(r => (string?)r.Attribute("Target") ?? "").ToList();

    public IEnumerable<SheetCell> ReadCells(SheetInfo sheet)
    {
        var doc = Load(sheet.PartPath);
        foreach (var c in doc.Descendants(Main + "c"))
        {
            var address = (string?)c.Attribute("r") ?? "";
            var type = (string?)c.Attribute("t");
            var f = c.Element(Main + "f");
            var v = (string?)c.Element(Main + "v");
            string? value = type switch
            {
                "s" when v is not null => SharedAt(v),
                "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(t => t.Value)),
                _ => v,
            };
            if (value is null && f is null) continue;
            yield return new SheetCell(address, value, f is null ? null : (f.Value.Length == 0 ? null : f.Value), (string?)f?.Attribute("si"), type == "e");
        }
    }

    /// <summary>The sheet as rows of cached values: row number, then column letters to value. Empty cells are absent.</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> ReadGrid(SheetInfo sheet)
    {
        var rows = new SortedDictionary<int, IReadOnlyDictionary<string, string>>();
        foreach (var c in ReadCells(sheet))
        {
            if (c.Value is null) continue;
            var split = c.Address.TakeWhile(char.IsLetter).Count();
            var col = c.Address[..split];
            var row = int.Parse(c.Address[split..], System.Globalization.CultureInfo.InvariantCulture);
            if (!rows.TryGetValue(row, out var r)) rows[row] = r = new Dictionary<string, string>(StringComparer.Ordinal);
            ((Dictionary<string, string>)r)[col] = c.Value;
        }
        return rows;
    }

    string SharedAt(string index) => int.TryParse(index, out var i) && i >= 0 && i < _shared.Value.Length ? _shared.Value[i] : "";

    string[] LoadSharedStrings()
    {
        var entry = _zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        return Load("xl/sharedStrings.xml").Root!.Elements(Main + "si").Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToArray();
    }

    IReadOnlyList<SheetInfo> LoadSheets()
    {
        var rels = Load("xl/_rels/workbook.xml.rels").Descendants(PkgRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!, StringComparer.Ordinal);
        return Load("xl/workbook.xml").Descendants(Main + "sheet").Select(s =>
        {
            var target = rels[(string)s.Attribute(Rel + "id")!];
            var part = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            return new SheetInfo((string)s.Attribute("name")!, (string?)s.Attribute("state") is "hidden" or "veryHidden", part);
        }).ToList();
    }

    XDocument Load(string part)
    {
        var entry = _zip.GetEntry(part) ?? throw new FileNotFoundException($"part missing in workbook: {part}");
        using var s = entry.Open();
        return XDocument.Load(s);
    }
}
