using RetailCore.Application.Foundation;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
namespace RetailCore.Application.Reporting;

public sealed class ReportService(ILocalTransactions transactions, FoundationService foundation, LocalSession session)
{
    public static readonly string[] Kinds = ["TradingSales", "TradingProducts", "TradingCategories", "TradingSettlement", "TradingShifts", "TradingCashHistory", "TradingStock", "TradingMovements", "TradingLowStock", "TradingAudit"];
    public Task<ReportTable> LoadAsync(string kind, ReportPeriod period, CancellationToken cancellationToken = default) => transactions.ReadAsync(async (store, ct) =>
    {
        var version = session.Version;
        await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
        if (!Kinds.Contains(kind) || period.From > period.Through || period.Through == DateOnly.MaxValue || period.Through.DayNumber - period.From.DayNumber > 366)
            throw new FoundationException("InvalidBillDates");
        var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
        bool InPeriod(DateTimeOffset at) { var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime); return d >= period.From && d <= period.Through; }
        DateTime Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, zone).DateTime;
        decimal Major(long minor) => minor / (decimal)shop.Currency.MinorUnitsPerMajor;
        var data = await store.TradingAsync(shop.Id, ct).ConfigureAwait(false);
        if (data.Sales.Any(s => s.CurrencyCode != shop.Currency.Code) || data.Refunds.Any(r => r.CurrencyCode != shop.Currency.Code)) throw new FoundationException("LocalConfiguration");
        var sales = data.Sales.Where(s => s.Status == SaleStatus.Completed || data.Refunds.Any(r => r.OriginalSaleId == s.Id && r.IsVoidReversal)).ToArray();
        var selected = sales.Where(s => InPeriod(s.CompletedAt!.Value)).ToArray();
        var refunds = data.Refunds.Where(r => InPeriod(r.CompletedAt!.Value)).ToArray();
        var products = await store.ProductsAsync(shop.Id, ct).ConfigureAwait(false);
        if (products.Any(p => p.UnitCost.Currency != shop.Currency)) throw new FoundationException("LocalConfiguration");
        var categories = await store.CategoriesAsync(shop.Id, ct).ConfigureAwait(false);
        var rows = new List<object?[]>(); string[] columns;
        // Quantity-boundary allocation makes historical cost reversals sum exactly even for tiny partial returns.
        var costs = new Dictionary<RetailCore.Domain.Common.RefundLineId, long>();
        var values = new Dictionary<RetailCore.Domain.Common.RefundLineId, long>();
        var taxesReturned = new Dictionary<RetailCore.Domain.Common.RefundLineId, long>();
        var entitlements = sales.ToDictionary(s => s.Id, ReturnAmounts.Entitlements);
        foreach (var original in sales)
            foreach (var line in original.ActiveLines)
            {
                long previous = 0, previousMoney = 0;
                var cap = entitlements[original.Id][line.Id];
                foreach (var r in data.Refunds.Where(r => r.OriginalSaleId == original.Id).OrderBy(r => r.CompletedAt).ThenBy(r => r.Id.Value))
                    foreach (var returned in r.Lines.Where(l => l.OriginalSaleLineId == line.Id))
                    {
                        var next = checked(previous + returned.Quantity.Thousandths);
                        costs[returned.Id] = ReturnAmounts.Share(line.CostAmount.MinorUnits, next, line.Quantity.Thousandths) - ReturnAmounts.Share(line.CostAmount.MinorUnits, previous, line.Quantity.Thousandths);
                        var nextMoney = checked(previousMoney + returned.Amount.MinorUnits);
                        // Monetary reversals follow actual credits; only cost follows quantity.
                        // A zero-collected line still reverses its original value by returned quantity.
                        var denominator = cap == 0 ? line.Quantity.Thousandths : cap;
                        var beforeValue = cap == 0 ? previous : previousMoney;
                        var afterValue = cap == 0 ? next : nextMoney;
                        values[returned.Id] = ReturnAmounts.Share(line.LineTotal.MinorUnits, afterValue, denominator) - ReturnAmounts.Share(line.LineTotal.MinorUnits, beforeValue, denominator);
                        taxesReturned[returned.Id] = ReturnAmounts.Share(line.TaxAmount.MinorUnits, afterValue, denominator) - ReturnAmounts.Share(line.TaxAmount.MinorUnits, beforeValue, denominator);
                        previousMoney = nextMoney;
                        previous = next;
                    }
            }
        static long Gross(SaleLine line) => line.NormalUnitPriceMinor is { } normal ? Money.FromMinor(normal, line.Currency).Extend(line.Quantity).MinorUnits : line.GrossAmount.MinorUnits;
        var gross = selected.Sum(s => s.ActiveLines.Sum(Gross)); var discount = gross - selected.Sum(s => s.Total.MinorUnits);
        var returnedTotal = refunds.Sum(r => r.Lines.Sum(l => values.GetValueOrDefault(l.Id))); var tax = selected.Sum(s => s.TaxTotal.MinorUnits) - refunds.Sum(r => r.Lines.Sum(l => taxesReturned.GetValueOrDefault(l.Id)));
        var net = selected.Sum(s => s.Total.MinorUnits) - returnedTotal;
        var historicalCost = selected.Sum(s => s.ActiveLines.Sum(l => l.CostAmount.MinorUnits)) - refunds.Sum(r => r.Lines.Sum(l => costs.GetValueOrDefault(l.Id)));
        if (kind == "TradingSales")
        {
            columns = ["GrossSales", "ExplicitDiscounts", "ReturnedValue", "NetSales", "NetTax", "HistoricalCost", "EstimatedProfit", "CashRounding", "ReturnRounding"];
            rows.Add([Major(gross), Major(discount), Major(returnedTotal), Major(net), Major(tax), Major(historicalCost), Major(net - tax - historicalCost), Major(selected.Sum(s => s.CashRoundingAdjustment.MinorUnits)), Major(refunds.Sum(r => r.Total.MinorUnits) - returnedTotal)]);
        }
        else if (kind is "TradingProducts" or "TradingCategories")
        {
            columns = [kind == "TradingProducts" ? "ReceiptName" : "CurrentCategory", "SoldQuantity", "ReturnedQuantity", "GrossSales", "ExplicitDiscounts", "ReturnedValue", "NetSales", "HistoricalCost", "EstimatedProfit"];
            var originalLines = sales.SelectMany(s => s.ActiveLines).ToDictionary(l => l.Id);
            string Group(RetailCore.Domain.Sales.SaleLine line) => kind == "TradingProducts" ? line.ProductId.Value + ":" + line.Unit : (line.CategoryIdSnapshot?.Value.ToString() ?? "") + ":" + line.Unit;
            var saleLines = selected.SelectMany(s => s.ActiveLines).ToArray(); var returnLines = refunds.SelectMany(r => r.Lines).ToArray();
            var groups = saleLines.Select(Group).Concat(returnLines.Where(l => originalLines.ContainsKey(l.OriginalSaleLineId)).Select(l => Group(originalLines[l.OriginalSaleLineId]))).Distinct();
            foreach (var group in groups)
            {
                var lines = saleLines.Where(l => Group(l) == group).ToArray();
                var returned = returnLines.Where(l => originalLines.TryGetValue(l.OriginalSaleLineId, out var original) && Group(original) == group).ToArray();
                var first = lines.FirstOrDefault() ?? originalLines[returned[0].OriginalSaleLineId];
                var label = kind == "TradingProducts" ? first.Description : categories.SingleOrDefault(c => c.Id == first.CategoryIdSnapshot) is { } category ? category.SinhalaName + " / " + category.Name : "UnclassifiedCategory";
                var total = lines.Sum(l => l.LineTotal.MinorUnits) - returned.Sum(l => values.GetValueOrDefault(l.Id));
                var cost = lines.Sum(l => l.CostAmount.MinorUnits) - returned.Sum(l => costs.GetValueOrDefault(l.Id));
                var taxes = lines.Sum(l => l.TaxAmount.MinorUnits) - returned.Sum(l => taxesReturned.GetValueOrDefault(l.Id));
                rows.Add([label, lines.Sum(l => l.Quantity.Value), returned.Sum(l => l.Quantity.Value), Major(lines.Sum(Gross)), Major(lines.Sum(l => Gross(l) - l.LineTotal.MinorUnits)), Major(returned.Sum(l => values.GetValueOrDefault(l.Id))), Major(total), Major(cost), Major(total - taxes - cost), "Unit" + first.Unit]);
            }
            columns = [.. columns, "Unit"];
        }
        else if (kind == "TradingSettlement")
        {
            columns = ["CashCollected", "CardCollected", "CashRefunded", "CardRefunded", "ExchangeCredit", "CashPayIn", "CashPayOut", "NetDrawerMovement"];
            var cash = selected.Sum(s => s.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount.MinorUnits));
            var card = selected.Sum(s => s.Payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount.MinorUnits));
            var cashBack = refunds.Sum(r => r.CashPaidMinor ?? (r.Method == PaymentMethod.Cash ? r.Total.MinorUnits : 0));
            var cardBack = refunds.Sum(r => r.CardPaidMinor ?? (r.Method == PaymentMethod.Card ? r.Total.MinorUnits : 0));
            var movements = data.Shifts.SelectMany(s => s.CashMovements).Where(m => InPeriod(m.OccurredAt)).ToArray();
            var payIn = movements.Where(m => m.Kind == RetailCore.Domain.Shifts.CashMovementKind.PayIn).Sum(m => m.Amount.MinorUnits);
            var payOut = movements.Where(m => m.Kind != RetailCore.Domain.Shifts.CashMovementKind.PayIn).Sum(m => m.Amount.MinorUnits);
            rows.Add([Major(cash), Major(card), Major(cashBack), Major(cardBack), Major(selected.Sum(s => s.Payments.Where(p => p.Method == PaymentMethod.ExchangeCredit).Sum(p => p.Amount.MinorUnits))), Major(payIn), Major(payOut), Major(cash - cashBack + payIn - payOut)]);
        }
        else if (kind == "TradingShifts")
        {
            columns = ["ShiftId", "OpenedLocal", "ClosedLocal", "OpeningFloat", "CashPayIn", "CashPayOut", "ExpectedCash", "CountedCash", "CashVariance", "Reason"];
            foreach (var shift in data.Shifts.Where(s => InPeriod(s.OpenedAt) || s.ClosedAt is { } at && InPeriod(at)).OrderBy(s => s.OpenedAt))
                rows.Add([shift.Id.Value.ToString(),Local(shift.OpenedAt),shift.ClosedAt is {} closed?Local(closed):null,shift.OpeningFloat.ToDecimal(),
                    Major(shift.CashMovements.Where(m=>m.Kind==RetailCore.Domain.Shifts.CashMovementKind.PayIn).Sum(m=>m.Amount.MinorUnits)),Major(shift.CashMovements.Where(m=>m.Kind!=RetailCore.Domain.Shifts.CashMovementKind.PayIn).Sum(m=>m.Amount.MinorUnits)),
                    shift.ExpectedCash?.ToDecimal() ?? (await store.ExpectedCashAsync(shift,ct).ConfigureAwait(false)).ToDecimal(),shift.CountedCash?.ToDecimal(),shift.Variance?.ToDecimal(),shift.ClosingNotes]);
        }
        else if (kind == "TradingCashHistory")
        {
            columns = ["LocalDate", "ShiftId", "CashPayIn", "CashPayOut", "Reason", "PerformedBy"];
            var users = await store.UsersAsync(shop.Id, ct).ConfigureAwait(false);
            foreach (var m in data.Shifts.SelectMany(s => s.CashMovements).Where(m => InPeriod(m.OccurredAt)).OrderBy(m => m.OccurredAt))
                rows.Add([Local(m.OccurredAt), m.ShiftId.Value.ToString(), m.Kind == RetailCore.Domain.Shifts.CashMovementKind.PayIn ? m.Amount.ToDecimal() : 0m, m.Kind == RetailCore.Domain.Shifts.CashMovementKind.PayIn ? 0m : m.Amount.ToDecimal(), m.Reason, users.SingleOrDefault(u => u.Id == m.RecordedBy)?.DisplayName ?? m.RecordedBy.Value.ToString()]);
        }
        else if (kind == "TradingAudit")
        {
            columns = ["LocalDate", "AuditAction", "PerformedBy", "ApprovedBy", "Reason", "EntityId"];
            var users = await store.UsersAsync(shop.Id, ct).ConfigureAwait(false);
            foreach (var a in data.Audit.Where(a => InPeriod(a.OccurredAt)).OrderBy(a => a.OccurredAt))
                rows.Add([Local(a.OccurredAt), "Audit" + a.Action, users.SingleOrDefault(u => u.Id == a.PerformedBy)?.DisplayName ?? a.PerformedBy?.Value.ToString() ?? RestoreActor(a,false), users.SingleOrDefault(u => u.Id == a.AuthorisedBy)?.DisplayName ?? a.AuthorisedBy?.Value.ToString() ?? RestoreActor(a,true), a.Summary ?? AuditReason(a.DetailsJson), a.EntityId.ToString()]);
        }
        else
        {
            var movements = await store.MovementsAsync(shop.Id, ct).ConfigureAwait(false);
            if (kind == "TradingMovements")
            {
                columns = ["LocalDate", "Sku", "ReceiptName", "MovementKind", "BaseQuantity", "Cost", "Reason", "Reference"];
                foreach (var m in movements.Where(m => InPeriod(m.OccurredAt)).OrderBy(m => m.OccurredAt))
                { var p = products.SingleOrDefault(p => p.Id == m.ProductId); rows.Add([Local(m.OccurredAt), p?.Sku, p?.ReceiptName, "Movement" + m.Kind, m.Quantity.Value * (m.Direction == StockDirection.In ? 1 : -1), m.UnitCost?.ToDecimal(), m.Reason, m.Reference]); }
            }
            else
            {
                columns = ["Sku", "ReceiptName", "BaseUnit", "StockBalance", "ReorderLevel", "RecordedUnitCost", "EstimatedStockValue"];
                // As-of end of selected local day, only underlying products: sealed packs never add another balance/value.
                foreach (var p in products.Where(p => p.StockBaseProductId is null))
                {
                    var balance = movements.Where(m => m.ProductId == p.Id && DateOnly.FromDateTime(Local(m.OccurredAt)) <= period.Through).Sum(m => m.Quantity.Thousandths * (m.Direction == StockDirection.In ? 1 : -1)) / 1000m;
                    if (kind == "TradingLowStock" && (!p.IsActive || balance > p.ReorderLevel.Value)) continue;
                    rows.Add([p.Sku, p.ReceiptName, "Unit" + p.Unit, balance, p.ReorderLevel.Value, p.UnitCost.ToDecimal(), decimal.Round(balance * p.UnitCost.ToDecimal(), shop.Currency.MinorUnitDigits, MidpointRounding.AwayFromZero)]);
                }
            }
        }
        if (kind is "TradingProducts" or "TradingCategories") rows = rows.OrderByDescending(r => (decimal)r[6]!).ToList();
        await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
        if (session.Version != version) throw new FoundationException("SignInRequired");
        return new ReportTable(kind, $"{period.From:yyyy-MM-dd} — {period.Through:yyyy-MM-dd} · {shop.TimeZoneId} · {shop.Currency.Code}", columns, rows);
    }, cancellationToken);
    private static string? RestoreActor(RetailCore.Domain.Security.AuditEntry entry,bool approval)
    {
        if(entry.EntityType!="DatabaseRestore" || entry.DetailsJson is null)return null;
        try
        {
            using var json=System.Text.Json.JsonDocument.Parse(entry.DetailsJson);
            var actor=json.RootElement.GetProperty("Actor");
            return actor.GetProperty(approval?"OwnerName":"Name").GetString();
        }
        catch(Exception ex) when(ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException){return null;}
    }
    private static string? AuditReason(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            static string? Find(System.Text.Json.JsonElement value)
            {
                if (value.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name.EndsWith("Reason", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == System.Text.Json.JsonValueKind.String) return property.Value.GetString();
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Object && Find(property.Value) is { } reason) return reason;
                }
                return null;
            }
            return Find(document.RootElement);
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

}
