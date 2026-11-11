using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
namespace RetailCore.Application.Reporting;

/// <summary>Shared Unicode exports. Literal text cells never become formulas; numeric cells retain their type.</summary>
public static class TableExport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static string SafeText(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || text.Any(c => c is '\t' or '\r' or '\n') || (trimmed.Length > 1 && trimmed[0] == '0' && char.IsDigit(trimmed[1]))) ? "'" + text : text;
    }
    public static string Display(object? value) => value switch { null => "", DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", Invariant), decimal n => n.ToString("0.############################", Invariant), _ => Convert.ToString(value, Invariant) ?? "" };
    public static byte[] Csv(ReportTable table)
    {
        static string Cell(object? value) => "\"" + (value is string s ? SafeText(s) : Display(value)).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var text = new StringBuilder();
        text.AppendLine(Cell(table.Title)); text.AppendLine(Cell(table.Context));
        text.AppendLine(string.Join(',', table.Columns.Select(c => Cell(c))));
        foreach (var row in table.Rows) text.AppendLine(string.Join(',', row.Select(Cell)));
        var encoding = new UTF8Encoding(true); return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
    }
    public static byte[] Xlsx(ReportTable table)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Part(string path, string xml) { using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); w.Write(xml); }
            Part("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Part("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Part("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Part("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Part("xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Nirmala UI\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"3\"><xf xfId=\"0\"/><xf numFmtId=\"22\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"49\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XElement Cell(object? value) => value switch
            {
                decimal n => new(ns + "c", new XAttribute("t", "n"), new XElement(ns + "v", n.ToString(Invariant))),
                DateTime d => new(ns + "c", new XAttribute("s", "1"), new XElement(ns + "v", d.ToOADate().ToString(Invariant))),
                _ => new(ns + "c", new XAttribute("t", "inlineStr"), new XAttribute("s", "2"), new XElement(ns + "is", new XElement(ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Display(value)))),
            };
            var rows = new List<object?[]> { new object?[] { table.Title }, new object?[] { table.Context }, table.Columns.Cast<object?>().ToArray() }; rows.AddRange(table.Rows);
            var worksheet = new XElement(ns + "worksheet", new XElement(ns + "sheetData", rows.Select((r, i) => new XElement(ns + "row", new XAttribute("r", i + 1), r.Select(Cell)))));
            Part("xl/worksheets/sheet1.xml", worksheet.ToString(SaveOptions.DisableFormatting));
        }
        return stream.ToArray();
    }
}
