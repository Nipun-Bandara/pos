using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;

namespace RetailCore.Application.Inventory;

public enum StockEntryKind { Receiving, Opening, AdjustmentIn, AdjustmentOut, Wastage, Count }
public sealed record StockEntryInput(Guid OperationId, ProductId ProductId, StockEntryKind Kind,
    decimal Quantity, decimal UnitCost, string Reason, DateTimeOffset OccurredAt, string? Reference = null,
    bool AllowNegative = false, string? ExpectedRevision = null, long? ExpectedBalanceThousandths = null);
public sealed record StockConfiguration(ProductId ProductId, ProductId BaseProductId, decimal Factor, decimal Reorder);
public sealed record StockRow(ProductId Id, string Sku, string SinhalaName, string EnglishName, UnitOfMeasure SellingUnit,
    ProductId BaseProductId, UnitOfMeasure BaseUnit, decimal Factor, decimal Balance, decimal Reorder,
    string Revision, bool Active)
{
    public string Display => $"{Sku} — {SinhalaName} / {EnglishName}";
    public bool LowStock => Id == BaseProductId && Active && Balance <= Reorder;
}
public sealed record StockHistory(StockMovementId Id, ProductId BaseProductId, ProductId EnteredProductId,
    DateTimeOffset OccurredAt, StockMovementKind Kind, decimal InputQuantity, decimal Factor, decimal SignedBaseQuantity,
    string BaseUnit, decimal? UnitCost, decimal? InputUnitCost, string? Reason, string? Reference,
    UserId? RecordedBy, UserId? ApprovedBy, string? SourceType, Guid? SourceId,
    string? RecordedByName, string? ApprovedByName);
public sealed record StockData(IReadOnlyList<StockRow> Products, IReadOnlyList<StockHistory> History, string Currency);
public sealed record ConsumptionInput(ProductId ProductId, decimal Quantity);
public sealed record ConsumptionPreview(ProductId BaseProductId, decimal Before, decimal Consumed, decimal After, string Revision)
{
    public bool Insufficient => After < 0;
}
public sealed record StockEntryResult(Guid OperationId, decimal Balance, decimal Variance);

public sealed class StockService(ILocalTransactions transactions, FoundationService foundation, IClock clock)
{
    public static ApprovalRequest EntryApproval(StockEntryInput input) => new("StockEntry", JsonSerializer.Serialize(input));
    public static ApprovalRequest SaleApproval(SaleCompleted sale, string reason, IReadOnlyDictionary<ProductId, string> revisions) =>
        new("NegativeStock", JsonSerializer.Serialize(new { sale.SaleId, sale.Items, Reason = reason,
            Revisions = revisions.OrderBy(p => p.Key.Value).Select(p => new { p.Key, p.Value }).ToArray() }));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Revision(IEnumerable<StockMovement> movements, ProductId product) =>
        Hash(string.Join('|', movements.Where(m => m.ProductId == product).Select(m => m.Id.Value.ToString("N")).Order(StringComparer.Ordinal)));

    public Task<StockData> LoadAsync(CancellationToken cancellationToken = default) => transactions.ReadAsync(async (store, ct) =>
    {
        var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
        var products = await store.ProductsAsync(actor.ShopId, ct).ConfigureAwait(false);
        var balances = await store.StockAsync(actor.ShopId, ct).ConfigureAwait(false);
        var history = await store.MovementsAsync(actor.ShopId, ct).ConfigureAwait(false);
        var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
        var currency = shop.Currency.Code;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
        var users = await store.UsersAsync(actor.ShopId, ct).ConfigureAwait(false);
        var rows = products.Select(p =>
        {
            var root = Root(products, p);
            return new StockRow(p.Id, p.Sku, p.SinhalaName, p.Name, p.Unit, root.Id, root.Unit,
                p.StockFactorThousandths / 1000m, balances.GetValueOrDefault(root.Id) / 1000m,
                root.ReorderLevel.Value, Revision(history, root.Id), p.IsActive);
        }).OrderBy(p => p.Sku, StringComparer.Ordinal).ToArray();
        return new StockData(rows, history.OrderBy(m => m.OccurredAt).ThenBy(m => m.Id.Value).Select(m =>
            new StockHistory(m.Id, m.ProductId, m.EnteredProductId ?? m.ProductId, TimeZoneInfo.ConvertTime(m.OccurredAt, zone), m.Kind,
                (m.InputQuantityThousandths > 0 ? m.InputQuantityThousandths : m.Quantity.Thousandths) / 1000m,
                m.ConversionFactorThousandths / 1000m, m.SignedQuantity, m.BaseUnit ?? products.SingleOrDefault(p => p.Id == m.ProductId)?.Unit.ToString() ?? "Unknown",
                m.UnitCost?.ToDecimal(), m.InputUnitCostMinor is { } cost && m.InputCostCurrency is { } code
                    ? Money.FromMinor(cost, Currency.FromCode(code)).ToDecimal() : null,
                m.Reason, m.Reference, m.RecordedBy, m.ApprovedBy, m.SourceType, m.SourceId,
                users.SingleOrDefault(u => u.Id == m.RecordedBy)?.DisplayName,
                users.SingleOrDefault(u => u.Id == m.ApprovedBy)?.DisplayName)).ToArray(), currency);
    }, cancellationToken);

    public Task ConfigureAsync(StockConfiguration input, CancellationToken cancellationToken = default) => transactions.WriteAsync(async (store, ct) =>
    {
        var actor = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
        var products = await store.ProductsAsync(actor.ShopId, ct).ConfigureAwait(false);
        var product = products.SingleOrDefault(p => p.Id == input.ProductId) ?? throw new FoundationException("InvalidProduct");
        var root = products.SingleOrDefault(p => p.Id == input.BaseProductId) ?? throw new FoundationException("InvalidProduct");
        if (root.StockBaseProductId is not null || !root.IsActive
            || (product.Id != root.Id && (product.Unit != UnitOfMeasure.Pack || input.Reorder != 0))
            || (product.Id == root.Id && products.Any(p => p.StockBaseProductId == product.Id) && input.Factor != 1))
            throw new FoundationException("InvalidStockConfiguration");
        var history = await store.MovementsAsync(actor.ShopId, ct).ConfigureAwait(false);
        if ((product.StockBaseProductId ?? product.Id) != root.Id || product.StockFactorThousandths != input.Factor * 1000m)
        {
            if (history.Any(m => m.ProductId == product.Id || m.EnteredProductId == product.Id)
                || products.Any(p => p.StockBaseProductId == product.Id))
                throw new FoundationException("StockMappingLocked");
        }
        try
        {
            product.ConfigureStock(root.Id, root.Unit, Quantity.Of(input.Factor).Thousandths, Quantity.Of(input.Reorder));
            if (root.Id != product.Id && root.StockBaseUnit is null)
                root.ConfigureStock(root.Id, root.Unit, 1000, root.ReorderLevel);
        }
        catch (DomainException) { throw new FoundationException("InvalidStockConfiguration"); }
        catch (OverflowException) { throw new FoundationException("InvalidStockConfiguration"); }
        store.Audit(AuditEntry.Record(actor.ShopId, AuditAction.StockConfigurationChanged, "Product", product.Id.Value,
            clock.UtcNow, actor.Id, detailsJson: JsonSerializer.Serialize(input)));
        return true;
    }, cancellationToken);

    public Task<IReadOnlyList<ConsumptionPreview>> PreviewConsumptionAsync(IReadOnlyList<ConsumptionInput> items,
        CancellationToken cancellationToken = default) => transactions.ReadAsync<IReadOnlyList<ConsumptionPreview>>(async (store, ct) =>
    {
        var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
        var products = await store.ProductsAsync(actor.ShopId, ct).ConfigureAwait(false);
        var history = await store.MovementsAsync(actor.ShopId, ct).ConfigureAwait(false);
        var balances = await store.StockAsync(actor.ShopId, ct).ConfigureAwait(false);
        try
        {
            var consumption = items.Select(i =>
            {
                var p = products.SingleOrDefault(p => p.Id == i.ProductId && p.IsActive) ?? throw new FoundationException("InvalidProduct");
                var q = Quantity.Of(i.Quantity);
                if (!p.Unit.AllowsFractionalQuantity() && !q.IsWholeUnits) throw new FoundationException("InvalidStockQuantity");
                return (Root: Root(products, p).Id, Quantity: StockConversion.ToBase(q, p.StockFactorThousandths).Thousandths);
            });
            return consumption.GroupBy(i => i.Root).Select(g => new ConsumptionPreview(g.Key,
                balances.GetValueOrDefault(g.Key) / 1000m, g.Sum(i => i.Quantity) / 1000m,
                checked(balances.GetValueOrDefault(g.Key) - g.Sum(i => i.Quantity)) / 1000m, Revision(history, g.Key))).ToArray();
        }
        catch (DomainException ex) { throw new FoundationException(ex.Message == "StockPrecision" ? "StockPrecision" : "InvalidStockQuantity"); }
        catch (OverflowException) { throw new FoundationException("InvalidStockQuantity"); }
    }, cancellationToken);

    public Task<StockEntryResult> PostAsync(StockEntryInput input, ApprovalTicket? ticket = null,
        CancellationToken cancellationToken = default)
    {
        var validateApproval = foundation.ApprovalValidator(ticket, EntryApproval(input));
        return transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
            var fingerprint = Hash(JsonSerializer.Serialize(new { input, Actor = actor.Id }));
            var prior = await store.CommandAsync(input.OperationId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.Kind != "StockEntry" || prior.Fingerprint != fingerprint) throw new FoundationException("OperationConflict");
                return JsonSerializer.Deserialize<StockEntryResult>(prior.Result)!;
            }
            if (input.OperationId == Guid.Empty || !Enum.IsDefined(input.Kind) || string.IsNullOrWhiteSpace(input.Reason)
                || input.Reason.Length > 500 || input.Reference?.Length > 200 || input.OccurredAt == default
                || input.OccurredAt > clock.UtcNow.AddMinutes(5)) throw new FoundationException("InvalidStockEntry");
            var products = await store.ProductsAsync(actor.ShopId, ct).ConfigureAwait(false);
            var product = products.SingleOrDefault(p => p.Id == input.ProductId && p.IsActive) ?? throw new FoundationException("InvalidProduct");
            var root = Root(products, product);
            var history = await store.MovementsAsync(actor.ShopId, ct).ConfigureAwait(false);
            if (root.StockBaseUnit is null) root.ConfigureStock(root.Id, root.Unit, 1000, root.ReorderLevel);
            var before = (await store.StockAsync(actor.ShopId, ct).ConfigureAwait(false)).GetValueOrDefault(root.Id);
            if (input.Kind == StockEntryKind.Count && (product.Id != root.Id || input.ExpectedRevision != Revision(history, root.Id)
                || input.ExpectedBalanceThousandths != before)) throw new FoundationException("StaleStockCount");
            if (input.Kind == StockEntryKind.Opening && history.Any(m => m.ProductId == root.Id)) throw new FoundationException("OpeningAlreadyRecorded");
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            long delta;
            Money cost;
            Quantity entered;
            var factor = product.StockFactorThousandths;
            try
            {
                cost = Money.FromMajor(input.UnitCost, shop.Currency);
                entered = Quantity.Of(input.Quantity);
                if (!product.Unit.AllowsFractionalQuantity() && !entered.IsWholeUnits) throw new DomainException("InvalidStockQuantity");
                if (cost.IsNegative) throw new DomainException("InvalidStockEntry");
                if (input.Kind == StockEntryKind.Count)
                {
                    delta = checked(entered.Thousandths - before);
                    entered = Quantity.FromThousandths(checked(Math.Abs(delta)));
                    factor = 1000;
                }
                else
                {
                    delta = StockConversion.ToBase(entered, factor).Thousandths;
                    if (input.Kind is StockEntryKind.AdjustmentOut or StockEntryKind.Wastage) delta = -delta;
                }
            }
            catch (DomainException ex) { throw new FoundationException(ex.Message == "StockPrecision" ? "StockPrecision" : "InvalidStockQuantity"); }
            catch (OverflowException) { throw new FoundationException("InvalidStockQuantity"); }
            var after = checked(before + delta);
            if (after < 0 && delta < 0 && !input.AllowNegative) throw new FoundationException("InsufficientStock");
            if (after < 0 && delta < 0 && input.ExpectedRevision != Revision(history, root.Id)) throw new FoundationException("StaleStockCount");
            UserId? approver = null;
            if (input.Kind is StockEntryKind.AdjustmentIn or StockEntryKind.AdjustmentOut or StockEntryKind.Wastage or StockEntryKind.Count
                || (after < 0 && delta < 0)) approver = await validateApproval(store, actor, ct).ConfigureAwait(false);
            var kind = input.Kind switch
            {
                StockEntryKind.Receiving => StockMovementKind.GoodsReceived,
                StockEntryKind.Opening => StockMovementKind.OpeningBalance,
                StockEntryKind.Wastage => StockMovementKind.Wastage,
                StockEntryKind.Count => StockMovementKind.StockTake,
                _ => StockMovementKind.Adjustment,
            };
            if (delta != 0)
            {
                var movement = StockMovement.Posted(actor.ShopId, product.Id, root.Id, entered, factor, root.Unit.ToString(),
                    delta > 0 ? StockDirection.In : StockDirection.Out, kind, "StockEntry", input.OperationId,
                    input.OccurredAt, actor.Id, input.Reason, cost, approver, input.Reference);
                await store.PostStockAsync(actor.ShopId, "StockEntry", input.OperationId, [movement], ct).ConfigureAwait(false);
            }
            var action = input.Kind == StockEntryKind.Count ? AuditAction.StockCounted
                : input.Kind is StockEntryKind.Receiving or StockEntryKind.Opening ? AuditAction.StockReceived : AuditAction.StockAdjusted;
            store.Audit(AuditEntry.Record(actor.ShopId, action, "Product", root.Id.Value, clock.UtcNow, actor.Id, approver,
                detailsJson: JsonSerializer.Serialize(new { input, Before = before, After = after, Factor = factor })));
            if (after < 0 && delta < 0)
                store.Audit(AuditEntry.Record(actor.ShopId, AuditAction.NegativeStockApproved, "Product", root.Id.Value,
                    clock.UtcNow, actor.Id, approver, detailsJson: JsonSerializer.Serialize(new { input.Reason, Before = before, After = after })));
            var result = new StockEntryResult(input.OperationId, after / 1000m, delta / 1000m);
            store.CompleteCommand(input.OperationId, "StockEntry", fingerprint, JsonSerializer.Serialize(result));
            return result;
        }, cancellationToken);
    }

    /// <summary>Database-only operation for milestone 04's enclosing sale transaction. The later event recognizes these same effects.</summary>
    public Func<ILocalStore, CancellationToken, Task> PrepareSalePosting(SaleCompleted sale, string reason,
        IReadOnlyDictionary<ProductId, string> revisions, ApprovalTicket? ticket = null)
    {
        ArgumentNullException.ThrowIfNull(sale);
        sale = sale with { Items = sale.Items.ToArray() };
        revisions = revisions.ToDictionary(p => p.Key, p => p.Value);
        var approval = foundation.ApprovalValidator(ticket, SaleApproval(sale, reason, revisions));
        return (store, ct) => PostSaleWithinAsync(store, sale, reason, revisions, approval, ct);
    }

    private async Task PostSaleWithinAsync(ILocalStore store, SaleCompleted sale, string reason,
        IReadOnlyDictionary<ProductId, string> revisions,
        Func<ILocalStore, User, CancellationToken, Task<UserId>> approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sale);
        var actor = await foundation.RequireUserAsync(store, false, cancellationToken).ConfigureAwait(false);
        if (sale.ShopId != actor.ShopId || sale.CashierId != actor.Id) throw new FoundationException("SignInRequired");
        var movements = StockPosting.Movements(sale.ShopId, "Sale", sale.SaleId.Value, sale.Items, StockDirection.Out,
            StockMovementKind.Sale, sale.OccurredAt, actor.Id, sale.Total.Currency, "Sale");
        var balances = await store.StockAsync(actor.ShopId, cancellationToken).ConfigureAwait(false);
        var history = await store.MovementsAsync(actor.ShopId, cancellationToken).ConfigureAwait(false);
        // Replays are checked by the ledger; do not reauthorize or deduct again.
        if (!history.Any(m => m.SourceType == "Sale" && m.SourceId == sale.SaleId.Value)
            && movements.GroupBy(m => m.ProductId).Any(g => balances.GetValueOrDefault(g.Key) < g.Sum(m => m.Quantity.Thousandths)))
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new FoundationException("InsufficientStock");
            foreach (var group in movements.GroupBy(m => m.ProductId))
                if (!revisions.TryGetValue(group.Key, out var revision) || revision != Revision(history, group.Key))
                    throw new FoundationException("StaleStockCount");
            var owner = await approval(store, actor, cancellationToken).ConfigureAwait(false);
            movements = StockPosting.Movements(sale.ShopId, "Sale", sale.SaleId.Value, sale.Items, StockDirection.Out,
                StockMovementKind.Sale, sale.OccurredAt, actor.Id, sale.Total.Currency, reason, owner);
            store.Audit(AuditEntry.Record(actor.ShopId, AuditAction.NegativeStockApproved, "Sale", sale.SaleId.Value,
                clock.UtcNow, actor.Id, owner, detailsJson: JsonSerializer.Serialize(new { reason, sale.Items })));
        }
        await store.PostStockAsync(actor.ShopId, "Sale", sale.SaleId.Value, movements, cancellationToken).ConfigureAwait(false);
    }

    private static Product Root(IReadOnlyList<Product> products, Product product)
    {
        var root = products.SingleOrDefault(p => p.Id == (product.StockBaseProductId ?? product.Id));
        return root is not null && root.StockBaseProductId is null ? root : throw new FoundationException("InvalidStockConfiguration");
    }
}
