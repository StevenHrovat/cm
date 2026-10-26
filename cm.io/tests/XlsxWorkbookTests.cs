using System.IO.Compression;
using cm.io.Spreadsheet;
using Xunit;

namespace cm.io.Tests;

public class XlsxWorkbookTests
{
    static MemoryStream Build()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string name, string xml) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(xml); }
            Part("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Calc" sheetId="1" r:id="rId1"/><sheet name="Hid" sheetId="2" state="hidden" r:id="rId2"/></sheets></workbook>""");
            Part("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Target="worksheets/sheet2.xml"/></Relationships>""");
            Part("xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>label</t></si></sst>""");
            Part("xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1"><v>2.5</v></c><c r="C1"><f>B1*2</f><v>5</v></c><c r="D1" t="e"><f>1/0</f><v>#DIV/0!</v></c></row></sheetData></worksheet>""");
            Part("xl/worksheets/sheet2.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData/></worksheet>""");
            Part("xl/externalLinks/_rels/externalLink1.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="file:///C:/x/y.xlsx"/></Relationships>""");
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Sheets_cells_formulas_errors_and_links_are_read_without_the_application()
    {
        using var wb = XlsxWorkbook.Open(Build());
        Assert.Equal(["Calc", "Hid"], wb.Sheets.Select(s => s.Name));
        Assert.True(wb.Sheets[1].Hidden);
        var cells = wb.ReadCells(wb.Sheets[0]).ToDictionary(c => c.Address);
        Assert.Equal("label", cells["A1"].Value);
        Assert.Equal("2.5", cells["B1"].Value);
        Assert.Equal("B1*2", cells["C1"].Formula);
        Assert.True(cells["D1"].IsError);
        Assert.Equal("#DIV/0!", cells["D1"].Value);
        Assert.Single(wb.ExternalLinkTargets());
    }
}
