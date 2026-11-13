using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using RetailCore.Application.Foundation;
using RetailCore.Application.Receipts;

namespace RetailCore.Desktop;

/// <summary>The same WPF pagination, shaped glyphs and vector barcode serve preview and Windows printing.</summary>
public static class ReceiptRenderer
{
    public static FlowDocument Render(ReceiptDocument receipt, int printableWidthMillimeters, bool copy, RetailCore.Domain.Sales.SaleStatus status = RetailCore.Domain.Sales.SaleStatus.Completed)
    {
        var width = printableWidthMillimeters * 96d / 25.4;
        if (printableWidthMillimeters is < 40 or > 300) throw new FoundationException("PrinterWidthUnsupported");
        var doc = new FlowDocument
        {
            Foreground = Brushes.Black, Background = Brushes.White,
            PageWidth = width, PageHeight = 1100, PagePadding = new Thickness(8), ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Nirmala UI, Iskoola Pota, Segoe UI"), FontSize = 13,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("si-LK"), FlowDirection = FlowDirection.LeftToRight,
        };
        foreach (var row in ReceiptLayout.Build(receipt, copy, status))
        {
            if (row.Style is ReceiptStyle.Columns or ReceiptStyle.ColumnsHeader or ReceiptStyle.Pair)
            {
                var pair = row.Style == ReceiptStyle.Pair;
                var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 2),
                    BorderBrush = Brushes.Black, BorderThickness = new Thickness(row.Style == ReceiptStyle.ColumnsHeader ? 1 : 0) };
                var columns = pair ? new[] { .48, .52 } : new[] { .20, .27, .27, .26 };
                foreach (var fraction in columns) table.Columns.Add(new TableColumn { Width = new GridLength((width - 20) * fraction) });
                var group = new TableRowGroup(); var line = new TableRow(); group.Rows.Add(line); table.RowGroups.Add(group);
                var values = pair ? new[] { row.Column1, row.Column2 } : new[] { row.Column1, row.Column2, row.Column3, row.Column4 };
                for (var i = 0; i < values.Length; i++)
                    line.Cells.Add(new TableCell(new Paragraph(new Run(values[i] ?? ""))
                    {
                        Margin = new Thickness(0), TextAlignment = pair && i == 0 ? TextAlignment.Left : TextAlignment.Right,
                        FontSize = pair ? 13 : 11,
                        FontWeight = row.Emphasis || (!pair && i == 2) ? FontWeights.Bold : FontWeights.Normal,
                    }) { Padding = new Thickness(1, 3, 1, 3) });
                doc.Blocks.Add(table); continue;
            }
            doc.Blocks.Add(new Paragraph(new Run(row.Text))
            {
                Margin = new Thickness(0, 2, 0, 2), FontWeight = row.Emphasis ? FontWeights.Bold : FontWeights.Normal,
                KeepTogether = true, KeepWithNext = row.Style == ReceiptStyle.ItemName,
                TextAlignment = row.Style is ReceiptStyle.Title or ReceiptStyle.Center or ReceiptStyle.Box ? TextAlignment.Center : TextAlignment.Left,
                FontSize = row.Style == ReceiptStyle.Title ? 24 : row.Style == ReceiptStyle.Box ? 17 : 13,
                BorderBrush = Brushes.Black,
                BorderThickness = row.Style == ReceiptStyle.Box ? new Thickness(1) : row.Style == ReceiptStyle.Rule ? new Thickness(0,0,0,1) : new Thickness(0),
                Padding = new Thickness(row.Style == ReceiptStyle.Box ? 4 : 0),
            });
        }
        var modules = BillBarcode.Encode(receipt.Number);
        var narrow = (width - 16) / modules.Count;
        if (narrow < .75) throw new FoundationException("PrinterWidthUnsupported");
        var barcode = new Canvas { Width = width - 16, Height = 64, Background = Brushes.White,
            UseLayoutRounding = false, SnapsToDevicePixels = false };
        for (var i = 0; i < modules.Count; i++)
        {
            if (!modules[i]) continue;
            var bar = new Rectangle { Width = narrow, Height = 52, Fill = Brushes.Black };
            Canvas.SetLeft(bar, i * narrow); barcode.Children.Add(bar);
        }
        doc.Blocks.Add(new BlockUIContainer(barcode) { Margin = new Thickness(0, 8, 0, 0) });
        doc.Blocks.Add(new Paragraph(new Run(receipt.Number)) { TextAlignment = TextAlignment.Center, KeepWithNext = false });
        return doc;
    }
}

/// <summary>One Windows spooler adapter, explicitly configured queue; never substitutes the default printer.</summary>
public sealed class WindowsReceiptPrinter(Dispatcher dispatcher) : IReceiptPrinter
{
    public async Task SendAsync(PrintPayload payload, CancellationToken cancellationToken)
    {
        await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = new LocalPrintServer();
            using var queue = server.GetPrintQueue(payload.Attempt.Printer);
            queue.Refresh();
            if (queue.IsOffline || queue.IsInError || queue.IsNotAvailable || queue.IsPaperJammed || queue.IsOutOfPaper)
                throw new InvalidOperationException("Printer is not ready.");
            var ticket = queue.DefaultPrintTicket.Clone();
            ticket.CopyCount = 1; ticket.PageScalingFactor = 100;
            var caps = queue.GetPrintCapabilities(ticket);
            var area = caps.PageImageableArea ?? throw new FoundationException("PrinterWidthUnsupported");
            var width = payload.Attempt.PrintableWidthMillimeters * 96d / 25.4;
            if (width > area.ExtentWidth + .1 || area.ExtentHeight < 96) throw new FoundationException("PrinterWidthUnsupported");
            var document = ReceiptRenderer.Render(payload.Receipt, payload.Attempt.PrintableWidthMillimeters, payload.Attempt.IsCopy, payload.Status);
            // Use the driver's media height; paginate rather than clipping a long bill.
            document.PageHeight = Math.Min(1100, area.ExtentHeight);
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            // Offset into the imageable region without shrinking the barcode or clipping margins.
            var positioned = new ImageablePaginator(paginator, area.OriginWidth, area.OriginHeight,
                caps.OrientedPageMediaWidth ?? area.ExtentWidth + area.OriginWidth,
                caps.OrientedPageMediaHeight ?? area.ExtentHeight + area.OriginHeight);
            PrintQueue.CreateXpsDocumentWriter(queue).Write(positioned, ticket);
        }, DispatcherPriority.Normal, cancellationToken);
    }

    private sealed class ImageablePaginator(DocumentPaginator inner, double x, double y, double width, double height) : DocumentPaginator
    {
        public override DocumentPage GetPage(int pageNumber)
        {
            var page = inner.GetPage(pageNumber);
            if (page == DocumentPage.Missing) return page;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(new VisualBrush(page.Visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null,
                    new Rect(x, y, page.Size.Width, page.Size.Height));
            return new DocumentPage(visual, new Size(width, height), new Rect(0,0,width,height), new Rect(x,y,page.Size.Width,page.Size.Height));
        }
        public override bool IsPageCountValid => inner.IsPageCountValid;
        public override int PageCount => inner.PageCount;
        public override Size PageSize { get => new(width,height); set => throw new NotSupportedException(); }
        public override IDocumentPaginatorSource Source => inner.Source;
    }
}
