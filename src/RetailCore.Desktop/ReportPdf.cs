using System.IO;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RetailCore.Application.Reporting;
namespace RetailCore.Desktop;

/// <summary>Uses the same Windows Unicode shaping as the UI. Pages are embedded at 192 dpi;
/// no dependency on a PDF printer, printer character set or the recipient's installed fonts.</summary>
public static class ReportPdf
{
    public static byte[] Render(ReportTable table)
    {
        const double width = 1056, height = 816, margin = 32;
        var pages = new List<byte[]>();
        for (var start = 1; start < Math.Max(2, table.Columns.Count); start += 5)
        {
            var indices = Enumerable.Repeat(0, 1).Concat(Enumerable.Range(start, Math.Min(5, table.Columns.Count - start))).ToArray();
            var cellWidth = (width - 2 * margin) / indices.Length;
            DrawingVisual? visual = null; DrawingContext? drawing = null; double y = 0;
            FormattedText Text(string text, double maxWidth, double size = 13) => new(text, CultureInfo.GetCultureInfo("si-LK"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Nirmala UI, Iskoola Pota, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 2)
            { MaxTextWidth = maxWidth, Trimming = TextTrimming.None };
            void Finish()
            {
                if (drawing is null) return;
                drawing.Close(); var bitmap = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual!);
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = new MemoryStream(); encoder.Save(output); pages.Add(output.ToArray()); drawing = null;
            }
            void Begin()
            {
                visual = new DrawingVisual(); drawing = visual.RenderOpen(); drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height)); y = margin;
                foreach (var text in new[] { table.Title, table.Context, $"{Texts.Get("ReportPage")} {pages.Count + 1} · {start}–{Math.Min(start + 5, table.Columns.Count)}/{table.Columns.Count}" })
                { var t = Text(text, width - 2 * margin); drawing.DrawText(t, new Point(margin, y)); y += t.Height + 6; }
                var headers = indices.Select(i => Text(table.Columns[i], cellWidth - 12)).ToArray(); var h = headers.Max(t => t.Height) + 14;
                foreach (var (t, i) in headers.Select((t, i) => (t, i))) drawing.DrawText(t, new Point(margin + i * cellWidth + 4, y + 4)); y += h;
                drawing.DrawLine(new Pen(Brushes.Black, 1), new Point(margin, y), new Point(width - margin, y));
            }
            Begin();
            foreach (var row in table.Rows)
            {
                var cells = indices.Select(i => Text(TableExport.Display(row[i]), cellWidth - 12)).ToArray();
                // Split unusually tall rows into continuation slices instead of clipping data.
                var rowHeight = cells.Max(t => t.Height) + 12; double consumed = 0;
                while (consumed < rowHeight)
                {
                    if (y >= height - margin - 20) { Finish(); Begin(); }
                    var slice = Math.Min(rowHeight - consumed, height - margin - y);
                    drawing!.PushClip(new RectangleGeometry(new Rect(margin, y, width - 2 * margin, slice)));
                    foreach (var (t, i) in cells.Select((t, i) => (t, i))) drawing.DrawText(t, new Point(margin + i * cellWidth + 4, y + 6 - consumed));
                    drawing.Pop(); y += slice; consumed += slice;
                }
                drawing!.DrawLine(new Pen(Brushes.LightGray, 0.5), new Point(margin, y), new Point(width - margin, y));
            }
            Finish();
        }
        return Encode(pages);
    }
    private static byte[] Encode(List<byte[]> pages)
    {
        using var stream = new MemoryStream(); var offsets = new List<long> { 0 };
        void Write(string value) { var bytes = Encoding.ASCII.GetBytes(value); stream.Write(bytes); }
        void Object(int id, string body) { offsets.Add(stream.Position); Write($"{id} 0 obj\n{body}\nendobj\n"); }
        Write("%PDF-1.4\n"); Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Count {pages.Count} /Kids [ {string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 3} 0 R"))} ] >>");
        for (var i = 0; i < pages.Count; i++)
        {
            var id = 3 + i * 3;
            Object(id, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /XObject << /Im {id + 1} 0 R >> >> /Contents {id + 2} 0 R >>");
            offsets.Add(stream.Position); Write($"{id + 1} 0 obj\n<< /Type /XObject /Subtype /Image /Width 2112 /Height 1632 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {pages[i].Length} >>\nstream\n"); stream.Write(pages[i]); Write("\nendstream\nendobj\n");
            const string content = "q 792 0 0 612 0 0 cm /Im Do Q\n"; Object(id + 2, $"<< /Length {content.Length} >>\nstream\n{content}endstream");
        }
        var xref = stream.Position; Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"); foreach (var at in offsets.Skip(1)) Write(at.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return stream.ToArray();
    }
}
