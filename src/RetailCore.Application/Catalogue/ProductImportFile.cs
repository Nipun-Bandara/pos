using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RetailCore.Application.Foundation;
using RetailCore.Application.Reporting;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;

namespace RetailCore.Application.Catalogue;

public sealed record ProductImportRow(int Row, ProductInput? Product, string? Error)
{
    public string Sku => Product?.Sku ?? "";
    public string Name => Product?.SinhalaName ?? "";
}
public sealed record ProductImportPreview(IReadOnlyList<ProductImportRow> Rows)
{
    public bool CanImport => Rows.Count > 0 && Rows.All(r => r.Error is null);
}

/// <summary>Bounded, literal-only UTF-8 CSV and XLSX import. One selected category per batch.</summary>
public static class ProductImportFile
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumRows = 5000;
    public static IReadOnlyList<string> Columns { get; } = Array.AsReadOnly(new[]
    {
        "Sku", "SinhalaName", "EnglishName", "ReceiptName", "Barcode", "Unit", "Cost", "NormalPrice",
        "Minimum1", "Price1", "Minimum2", "Price2", "Minimum3", "Price3", "Active"
    });
    public static byte[] Template(string extension, string title = "RetailCore product import",
        string instructions = "Replace the example; keep headers. Select category in Products. Decimal point: .")
    {
        object?[] sample = ["TEA-001", "තේ", "Tea", "තේ පැකට්", "001234567890", "Each", 50m, 100m, 2m, 90m, 3m, 85m, 4m, 80m, "true"];
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return TableExport.Xlsx(new ReportTable(title, instructions, Columns, [sample]));
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)) throw new FoundationException("InvalidImportFile");
        var text = string.Join(',', Columns) + "\r\n" + string.Join(',', sample.Select(v => "\"" + TableExport.Display(v).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + "\r\n";
        return [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];
    }
    public static IReadOnlyList<ProductImportRow> Read(byte[] bytes, string extension, CategoryId category)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new FoundationException("InvalidImportFile");
        try
        {
            var rows = extension.ToLowerInvariant() switch
            {
                ".csv" => Csv(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')),
                ".xlsx" => Xlsx(bytes),
                _ => throw new FoundationException("InvalidImportFile")
            };
            var header = rows.FindIndex(r => r.Cells.SequenceEqual(Columns));
            if (header < 0 || header > 2 || (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && header != 0))
                throw new FoundationException("ImportHeaders");
            var data = rows.Skip(header + 1).Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToArray();
            if (data.Length == 0 || data.Length > MaximumRows) throw new FoundationException("InvalidImportFile");
            return data.Select(r => Parse(r.Number, r.Cells, category)).ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or XmlException or ArgumentException or OverflowException or InvalidOperationException or FormatException)
        { throw new FoundationException("InvalidImportFile"); }
    }
    private static ProductImportRow Parse(int number, string[] cells, CategoryId category)
    {
        try
        {
            if (cells.Length != Columns.Count || cells.Any(c => c.Length > 2000)) throw new FoundationException("InvalidImportFile");
            decimal Number(int i) => decimal.TryParse(cells[i], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) ? value : throw new FoundationException("InvalidNumber");
            var bands = new List<BandInput>(); var gap = false;
            for (var i = 8; i < 14; i += 2)
            {
                if (string.IsNullOrWhiteSpace(cells[i]) && string.IsNullOrWhiteSpace(cells[i + 1])) { gap = true; continue; }
                if (gap) throw new FoundationException("InvalidPricing");
                bands.Add(new BandInput(Number(i), Number(i + 1)));
            }
            if (!Enum.TryParse<UnitOfMeasure>(cells[5], false, out var unit) || !Enum.IsDefined(unit) || cells[5] != unit.ToString())
                throw new FoundationException("ImportUnit");
            if (!bool.TryParse(cells[14], out var active)) throw new FoundationException("InvalidProduct");
            return new(number, new ProductInput(null, cells[0], cells[1], cells[2], cells[3], category, unit,
                Number(6), Number(7), cells[4], bands, active), null);
        }
        catch (FoundationException ex) { return new(number, null, ex.Key); }
    }
    private sealed record FileRow(int Number, string[] Cells);
    private static List<FileRow> Csv(string text)
    {
        var rows = new List<FileRow>(); var cells = new List<string>(); var cell = new StringBuilder();
        var quoted = false; var closed = false; var atStart = true;
        void Field() { cells.Add(cell.ToString().Trim()); cell.Clear(); closed = false; atStart = true; }
        void Row()
        {
            Field(); rows.Add(new(rows.Count + 1, cells.ToArray())); cells.Clear();
            if (rows.Count > MaximumRows + 3) throw new FoundationException("InvalidImportFile");
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                else cell.Append(c);
            }
            else if (c == ',') { Field(); if (cells.Count >= Columns.Count) throw new FoundationException("InvalidImportFile"); }
            else if (c is '\r' or '\n') { Row(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else if (closed) throw new FoundationException("InvalidImportFile");
            else if (c == '"' && atStart) { quoted = true; atStart = false; }
            else if (c == '"') throw new FoundationException("InvalidImportFile");
            else { cell.Append(c); atStart = false; }
            if (cell.Length > 2000) throw new FoundationException("InvalidImportFile");
        }
        if (quoted) throw new FoundationException("InvalidImportFile");
        if (cells.Count > 0 || cell.Length > 0 || closed) Row();
        return rows;
    }
    private static List<FileRow> Xlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 100 || zip.Entries.Sum(e => e.Length) > 25 * 1024 * 1024 ||
            zip.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))) throw new FoundationException("InvalidImportFile");
        XDocument? Part(string path, bool required = true)
        {
            var entry = zip.GetEntry(path);
            if (entry is null) return required ? throw new FoundationException("InvalidImportFile") : null;
            using var input = entry.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 25 * 1024 * 1024 });
            return XDocument.Load(reader);
        }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var workbook = Part("xl/workbook.xml")!;
        var sheets = workbook.Descendants(ns + "sheet").ToArray();
        if (sheets.Length != 1) throw new FoundationException("InvalidImportFile");
        var id = (string?)sheets[0].Attribute(rel + "id");
        var relationships = Part("xl/_rels/workbook.xml.rels")!.Root!.Elements().ToArray();
        if (relationships.Any(r => (string?)r.Attribute("TargetMode") == "External")) throw new FoundationException("InvalidImportFile");
        var target = (string?)relationships.SingleOrDefault(r => (string?)r.Attribute("Id") == id)?.Attribute("Target");
        if (target is null || target.Contains("..", StringComparison.Ordinal) || target.Contains('\\')) throw new FoundationException("InvalidImportFile");
        var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        var shared = Part("xl/sharedStrings.xml", false)?.Descendants(ns + "si")
            .Select(s => string.Concat(s.Descendants(ns + "t").Select(t => t.Value))).ToArray() ?? [];
        var rows = new List<FileRow>();
        foreach (var row in Part(path)!.Descendants(ns + "row"))
        {
            if (rows.Count >= MaximumRows + 3) throw new FoundationException("InvalidImportFile");
            var values = new string[Columns.Count]; Array.Fill(values, ""); var index = 0;
            foreach (var cell in row.Elements(ns + "c"))
            {
                if (cell.Element(ns + "f") is not null) throw new FoundationException("ImportFormulas");
                var reference = (string?)cell.Attribute("r");
                if (reference is not null)
                {
                    index = 0;
                    foreach (var c in reference.TakeWhile(char.IsAsciiLetterUpper)) index = checked(index * 26 + c - 'A' + 1);
                    index--;
                }
                if (index < 0 || index >= Columns.Count) throw new FoundationException("InvalidImportFile");
                var type = (string?)cell.Attribute("t"); var value = cell.Element(ns + "v")?.Value ?? "";
                value = type switch
                {
                    "inlineStr" => string.Concat(cell.Element(ns + "is")?.Descendants(ns + "t").Select(t => t.Value) ?? []),
                    "b" when index == 14 => value == "1" ? "true" : value == "0" ? "false" : throw new FoundationException("InvalidImportFile"),
                    "s" => int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < shared.Length ? shared[sharedIndex] : throw new FoundationException("InvalidImportFile"),
                    null or "n" => value,
                    _ => throw new FoundationException("InvalidImportFile")
                };
                // Identifiers must be stored as text: numeric spreadsheet cells can already have lost zeroes.
                if (index is 0 or 4 && value.Length > 0 && (type is null or "n")) throw new FoundationException("ImportTextIdentifiers");
                if (value.Length > 2000) throw new FoundationException("InvalidImportFile");
                values[index++] = value.Trim();
            }
            var number = (int?)row.Attribute("r") ?? rows.Count + 1;
            rows.Add(new(number, values));
        }
        return rows;
    }
}
