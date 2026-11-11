using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;

namespace RetailCore.Application.Receipts;

public sealed class ReceiptService(ILocalTransactions transactions, FoundationService foundation, LocalSession session, IClock clock)
{
    public Task<ReceiptSettings> SettingsAsync(CancellationToken cancellationToken = default) => transactions.ReadAsync(async (store, ct) =>
    {
        await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
        var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
        return new ReceiptSettings(shop.PaperWidthMillimeters, shop.PrinterName,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId)).DateTime));
    }, cancellationToken);
    public Task<IReadOnlyList<BillSummary>> HistoryAsync(BillFilter filter, CancellationToken cancellationToken = default) =>
        transactions.ReadAsync(async (store, ct) =>
        {
            await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            if (filter.From > filter.Through || filter.Through.DayNumber - filter.From.DayNumber > 366 || filter.Through == DateOnly.MaxValue)
                throw new FoundationException("InvalidBillDates");
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
            var from = LocalBoundary(filter.From, zone); var until = LocalBoundary(filter.Through.AddDays(1), zone);
            string? number = null;
            if (!string.IsNullOrWhiteSpace(filter.Number)) number = Normalize(filter.Number);
            var rows = await store.BillsAsync(shop.Id, from, until, number, ct).ConfigureAwait(false);
            return (IReadOnlyList<BillSummary>)rows.Select(r => r with { LocalTime = TimeZoneInfo.ConvertTime(r.CompletedAt, zone)
                .ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture) }).ToArray();
        }, cancellationToken);

    // A scanned/manual exact number deliberately ignores the history date filter.
    public Task<BillDetail> FindAsync(string number, CancellationToken cancellationToken = default) =>
        DetailAsync(null, Normalize(number), cancellationToken);
    public Task<BillDetail> DetailAsync(Guid saleId, CancellationToken cancellationToken = default) => DetailAsync(saleId, null, cancellationToken);
    private Task<BillDetail> DetailAsync(Guid? id, string? number, CancellationToken cancellationToken) =>
        transactions.WriteAsync(async (store, ct) =>
        {
            await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var sale = await store.SavedSaleAsync(shop.Id, id, number, ct).ConfigureAwait(false) ?? throw new FoundationException("BillNotFound");
            var receipt = await store.ReceiptAsync(shop.Id, sale.Id.Value, ct).ConfigureAwait(false);
            if (receipt is null)
            {
                // Legacy sales predate header snapshots. Never invent historical names or edit the sale.
                var users = await store.UsersAsync(shop.Id, ct).ConfigureAwait(false);
                receipt = ReceiptDocument.Capture(sale, shop, users.SingleOrDefault(u => u.Id == sale.CashierId)?.DisplayName ?? "", true);
                store.AddReceipt(shop.Id, receipt);
            }
            return new BillDetail(receipt, sale.Status, await store.PrintsAsync(shop.Id, sale.Id.Value, ct).ConfigureAwait(false),await store.ReturnsAsync(shop.Id,sale.Id.Value,ct).ConfigureAwait(false));
        }, cancellationToken);

    public async Task<PrintResult> PrintAsync(Guid saleId, Guid attemptId, IReceiptPrinter printer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (attemptId == Guid.Empty) throw new FoundationException("PrintConflict");
        var detail = await DetailAsync(saleId, cancellationToken).ConfigureAwait(false);
        var version = session.Version;
        var payload = await transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            if (session.Version != version) throw new FoundationException("SignInRequired");
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var prior = await store.PrintAttemptAsync(shop.Id, attemptId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.SaleId != saleId || prior.ActorId != actor.Id.Value) throw new FoundationException("PrintConflict");
                return (PrintPayload?)null; // Never automatically resubmit a possibly printed job.
            }
            if (string.IsNullOrWhiteSpace(shop.PrinterName)) throw new FoundationException("PrinterNotConfigured");
            _ = BillBarcode.Encode(detail.Receipt.Number); // fail before logging a job if barcode cannot be rendered
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(detail.Receipt))));
            var earlier = await store.PrintsAsync(shop.Id, saleId, ct).ConfigureAwait(false);
            var attempt = new PrintAttempt(attemptId, saleId, actor.Id.Value, clock.UtcNow, shop.PrinterName,
                shop.PaperWidthMillimeters, earlier.Count > 0, hash);
            store.AddPrintAttempt(shop.Id, attempt);
            store.Audit(AuditEntry.Record(shop.Id, AuditAction.ReceiptPrintRequested, "Sale", saleId, clock.UtcNow, actor.Id,
                detailsJson: JsonSerializer.Serialize(new { attempt.Id, attempt.Printer, attempt.IsCopy, Template = ReceiptDocument.TemplateFor(detail.Receipt.Version), hash })));
            var saved = await store.SavedSaleAsync(shop.Id, saleId, null, ct).ConfigureAwait(false) ?? throw new FoundationException("BillNotFound");
            return new PrintPayload(detail.Receipt, attempt, saved.Status);
        }, cancellationToken).ConfigureAwait(false);
        if (payload is null) return new PrintResult(attemptId, "PrintAlreadyRequested");
        // Printing is outside the sale/stock transaction and is never automatically retried.
        string status;
        try
        {
            await transactions.ReadAsync(async (store, ct) =>
            {
                await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
                if (session.Version != version) throw new FoundationException("SignInRequired");
                return true;
            }, cancellationToken).ConfigureAwait(false);
            await printer.SendAsync(payload, cancellationToken).ConfigureAwait(false);
            status = "PrintSpoolAccepted";
        }
        catch (FoundationException ex) when (ex.Key == "PrinterWidthUnsupported") { status = ex.Key; }
        catch (Exception) { status = "PrintFailed"; } // Never persist driver messages or sensitive system paths.
        // Persist the result even if cancellation/sign-out happened while Windows submitted the job.
        // The authenticated requester was frozen above; this cannot create financial writes.
        await transactions.WriteAsync(async (store, ct) =>
        {
            store.AddPrintOutcome(new PrintOutcome(attemptId, clock.UtcNow, status));
            store.Audit(AuditEntry.Record(new ShopId(await store.PrintShopAsync(attemptId, ct).ConfigureAwait(false)),
                AuditAction.ReceiptPrintFinished, "Sale", saleId, clock.UtcNow, new UserId(payload.Attempt.ActorId),
                detailsJson: JsonSerializer.Serialize(new { attemptId, status })));
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
        return new PrintResult(attemptId, status);
    }

    public Task<ReturnPrintInfo> ReturnInfoAsync(Guid id,CancellationToken cancellationToken=default)=>transactions.ReadAsync(async(store,ct)=>
    {
        var actor=await foundation.RequireUserAsync(store,false,ct).ConfigureAwait(false);
        var result=await store.ReturnAsync(actor.ShopId,id,ct).ConfigureAwait(false)??throw new FoundationException("BillNotFound");
        return new ReturnPrintInfo(result.Document,await store.ReturnPrintsAsync(actor.ShopId,id,ct).ConfigureAwait(false));
    },cancellationToken);
    public async Task<PrintResult> PrintReturnAsync(Guid id,Guid attemptId,IReceiptPrinter printer,CancellationToken cancellationToken=default)
    {
        if(attemptId==Guid.Empty)throw new FoundationException("PrintConflict");
        var version=session.Version;
        var payload=await transactions.WriteAsync(async(store,ct)=>
        {
            var actor=await foundation.RequireUserAsync(store,false,ct).ConfigureAwait(false);
            if(session.Version!=version)throw new FoundationException("SignInRequired");
            var result=await store.ReturnAsync(actor.ShopId,id,ct).ConfigureAwait(false)??throw new FoundationException("BillNotFound");
            var prior=await store.ReturnPrintAsync(actor.ShopId,attemptId,ct).ConfigureAwait(false);
            if(prior is not null)
            {
                if(prior.SaleId!=id || prior.ActorId!=actor.Id.Value)throw new FoundationException("PrintConflict");
                return (PrintPayload?)null;
            }
            var shop=(await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            if(string.IsNullOrWhiteSpace(shop.PrinterName))throw new FoundationException("PrinterNotConfigured");
            _=BillBarcode.Encode(result.Number);
            var history=await store.ReturnPrintsAsync(shop.Id,id,ct).ConfigureAwait(false);
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result.Document))));
            var attempt=new PrintAttempt(attemptId,id,actor.Id.Value,clock.UtcNow,shop.PrinterName,shop.PaperWidthMillimeters,history.Count>0,hash);
            store.AddReturnPrint(shop.Id,attempt);
            store.Audit(AuditEntry.Record(shop.Id,AuditAction.ReceiptPrintRequested,"Return",id,clock.UtcNow,actor.Id,
                detailsJson:JsonSerializer.Serialize(new {attempt.Id,attempt.IsCopy,attempt.Printer,hash})));
            return new PrintPayload(result.Document,attempt,SaleStatus.Completed);
        },cancellationToken).ConfigureAwait(false);
        if(payload is null)return new(attemptId,"PrintAlreadyRequested");
        string status;
        try
        {
            await transactions.ReadAsync(async(store,ct)=>
            {
                await foundation.RequireUserAsync(store,false,ct).ConfigureAwait(false);
                if(session.Version!=version)throw new FoundationException("SignInRequired");return true;
            },cancellationToken).ConfigureAwait(false);
            await printer.SendAsync(payload,cancellationToken).ConfigureAwait(false);status="PrintSpoolAccepted";
        }
        catch(FoundationException ex)when(ex.Key=="PrinterWidthUnsupported"){status=ex.Key;}
        catch(Exception){status="PrintFailed";}
        await transactions.WriteAsync(async(store,ct)=>
        {
            var shop=(await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            store.AddReturnPrintOutcome(new(attemptId,clock.UtcNow,status));
            store.Audit(AuditEntry.Record(shop.Id,AuditAction.ReceiptPrintFinished,"Return",id,clock.UtcNow,new UserId(payload.Attempt.ActorId),
                detailsJson:JsonSerializer.Serialize(new {attemptId,status})));return true;
        },CancellationToken.None).ConfigureAwait(false);
        return new(attemptId,status);
    }

    private static string Normalize(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new FoundationException("InvalidBillNumber");
        try { return ReceiptNumber.Parse(number.Trim()).ToString(); }
        catch (Exception ex) when (ex is DomainException or ArgumentException) { throw new FoundationException("InvalidBillNumber"); }
    }
    private static DateTimeOffset LocalBoundary(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones skip midnight; advance to the first valid minute of that local date.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
