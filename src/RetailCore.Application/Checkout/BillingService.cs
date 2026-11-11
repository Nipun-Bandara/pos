using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Application.Inventory;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Tax;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Application.Checkout;

/// <summary>Recoverable carts and atomic, authenticated checkout. No payment gateway calls.</summary>
public sealed class BillingService(ILocalTransactions transactions, FoundationService foundation, LocalSession session, IClock clock)
{
    private sealed record Context(User Actor, Shop Shop, Terminal Terminal, Shift? Shift, BasketDocument Basket,
        IReadOnlyList<Product> Products, IReadOnlyList<TaxRate> Rates, IReadOnlyDictionary<ProductId, long> Balances,
        IReadOnlyList<StockMovement> Movements);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static ApprovalRequest CompletionApproval(CompletionRequest request) => new("Checkout", JsonSerializer.Serialize(request));

    public Task<BillingState> LoadAsync(CancellationToken cancellationToken = default) => transactions.WriteAsync(async (store, ct) =>
        await StateAsync(store, await ContextAsync(store, ct).ConfigureAwait(false), ct).ConfigureAwait(false), cancellationToken);

    public Task<ShiftId> OpenShiftAsync(Guid operationId, decimal openingFloat, CancellationToken cancellationToken = default) =>
        transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var terminal = (await store.TerminalsAsync(shop.Id, ct).ConfigureAwait(false)).Single(t => t.IsActive);
            if (operationId == Guid.Empty) throw new FoundationException("InvalidFloat");
            Money amount;
            try { amount = Money.FromMajor(openingFloat, shop.Currency); }
            catch (DomainException) { throw new FoundationException("InvalidFloat"); }
            catch (OverflowException) { throw new FoundationException("InvalidFloat"); }
            if (amount.IsNegative) throw new FoundationException("InvalidFloat");
            var fingerprint = Hash(new { Actor = actor.Id, Terminal = terminal.Id, amount.MinorUnits });
            var prior = await store.CommandAsync(operationId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.Kind != "OpenShift" || prior.Fingerprint != fingerprint) throw new FoundationException("OperationConflict");
                return JsonSerializer.Deserialize<ShiftId>(prior.Result);
            }
            if (await store.OpenShiftAsync(shop.Id, terminal.Id, ct).ConfigureAwait(false) is not null)
                throw new FoundationException("ShiftAlreadyOpen");
            var shift = Shift.Open(shop.Id, terminal.Id, actor.Id, amount, clock.UtcNow);
            store.Add(shift);
            store.Audit(AuditEntry.Record(shop.Id, AuditAction.ShiftOpened, "Shift", shift.Id.Value, clock.UtcNow, actor.Id,
                detailsJson: JsonSerializer.Serialize(new { OpeningFloatMinor = amount.MinorUnits, shop.Currency.Code })));
            store.CompleteCommand(operationId, "OpenShift", fingerprint, JsonSerializer.Serialize(shift.Id));
            return shift.Id;
        }, cancellationToken);

    public Task<BillingState> ScanAsync(Guid basketId, long version, string barcode, decimal quantity = 1,
        CancellationToken cancellationToken = default) => transactions.WriteAsync(async (store, ct) =>
    {
        var c = await ContextAsync(store, ct).ConfigureAwait(false);
        RequireVersion(c.Basket, basketId, version);
        var matches = c.Products.Where(p => p.IsActive && p.HasBarcode(barcode.Trim())).ToArray();
        if (matches.Length != 1) throw new FoundationException(matches.Length == 0 ? "BarcodeNotFound" : "AmbiguousBarcode");
        var p = matches[0];
        var added = ParseQuantity(quantity, p.Unit);
        var previous = c.Basket.Items.SingleOrDefault(i => i.ProductId == p.Id);
        long combined;
        try { combined = checked((previous?.QuantityThousandths ?? 0) + added.Thousandths); }
        catch (OverflowException) { throw new FoundationException("InvalidBillingQuantity"); }
        var next = new BasketItem(p.Id, combined,
            previous?.RequestedUnitPriceMinor, previous?.Reason);
        ValidateItem(p, next);
        var items = c.Basket.Items.Where(i => i.ProductId != p.Id).ToList();
        // Replace at the original position; a repeated scan is an increment, never a duplicate suppression.
        if (previous is null) items.Add(next);
        else { items = c.Basket.Items.Select(i => i.ProductId == p.Id ? next : i).ToList(); }
        return await SaveAsync(store, c, items, ct).ConfigureAwait(false);
    }, cancellationToken);

    public Task<BillingState> EditAsync(BasketEdit input, CancellationToken cancellationToken = default) => transactions.WriteAsync(async (store, ct) =>
    {
        var c = await ContextAsync(store, ct).ConfigureAwait(false);
        RequireVersion(c.Basket, input.BasketId, input.Version);
        var items = c.Basket.Items.ToList();
        var index = items.FindIndex(i => i.ProductId == input.ProductId);
        if (input.Remove)
        {
            if (index < 0) throw new FoundationException("SelectBillingLine");
            items.RemoveAt(index);
        }
        else
        {
            var p = c.Products.SingleOrDefault(p => p.Id == input.ProductId && p.IsActive) ?? throw new FoundationException("BillingProductUnavailable");
            long? price = null;
            try { if (input.RequestedUnitPrice is { } value) price = Money.FromMajor(value, c.Shop.Currency).MinorUnits; }
            catch (DomainException) { throw new FoundationException("InvalidManualPrice"); }
            catch (OverflowException) { throw new FoundationException("InvalidManualPrice"); }
            var item = new BasketItem(p.Id, ParseQuantity(input.Quantity, p.Unit).Thousandths, price, price is null ? null : input.Reason?.Trim());
            ValidateItem(p, item);
            if (index < 0) items.Add(item); else items[index] = item;
        }
        return await SaveAsync(store, c, items, ct).ConfigureAwait(false);
    }, cancellationToken);

    public Task<BillingState> ClearAsync(Guid basketId, long version, CancellationToken cancellationToken = default) =>
        transactions.WriteAsync(async (store, ct) =>
        {
            var c = await ContextAsync(store, ct).ConfigureAwait(false); RequireVersion(c.Basket, basketId, version);
            var cart = BasketDocument.Empty(c.Basket.LastRequest, c.Basket.LastResult);
            store.SaveBasket(c.Shop.Id, c.Terminal.Id, c.Actor.Id, cart);
            return await StateAsync(store, c with { Basket = cart }, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<CompletionResult> CompleteAsync(CompletionRequest request, ApprovalTicket? ticket = null, CancellationToken cancellationToken = default)
    {
        var validateApproval = foundation.ApprovalValidator(ticket, CompletionApproval(request));
        var sessionVersion = session.Version;
        return transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            if (session.Version != sessionVersion) throw new FoundationException("SignInRequired");
            ValidatePaymentInput(request);
            var fingerprint = Hash(new { Actor = actor.Id, Request = request });
            var prior = await store.CommandAsync(request.BasketId, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.Kind != "CompleteSale" || prior.Fingerprint != fingerprint) throw new FoundationException("OperationConflict");
                return JsonSerializer.Deserialize<CompletionResult>(prior.Result)!;
            }
            var c = await ContextAsync(store, ct).ConfigureAwait(false);
            RequireVersion(c.Basket, request.BasketId, request.Version);
            if (c.Shift is null) throw new FoundationException("OpenShiftRequired");
            var quote = Quote(c);
            if (quote.Lines.Count == 0) throw new FoundationException("BasketEmpty");
            if (request.QuoteHash != quote.Hash) throw new FoundationException("CheckoutChanged");
            if (quote.NeedsStockApproval && (!request.AllowNegative || string.IsNullOrWhiteSpace(request.NegativeReason)))
                throw new FoundationException("InsufficientStock");
            if (request.Method == PaymentMethod.Cash && request.TenderedMinor < quote.CashDueMinor) throw new FoundationException("InsufficientTender");
            if (request.Method == PaymentMethod.Card && (request.TenderedMinor != quote.TotalMinor || quote.TotalMinor <= 0))
                throw new FoundationException("InvalidTender");
            UserId? owner = null;
            if (quote.NeedsDiscountApproval || quote.NeedsStockApproval)
                owner = await validateApproval(store, actor, ct).ConfigureAwait(false);
            var sale = BuildSale(c, owner);
            var tendered = Money.FromMinor(request.TenderedMinor, c.Shop.Currency);
            if (request.Method == PaymentMethod.Cash)
            {
                sale.AddCashPayment(tendered, clock.UtcNow, c.Shop.CashIncrementMinor);
            }
            else
            {
                sale.AddPayment(PaymentMethod.Card, sale.Total, request.CardReference, clock.UtcNow);
            }
            if (request.Method == PaymentMethod.Cash)
            {
                try
                {
                    var expected = await store.ExpectedCashAsync(c.Shift, ct).ConfigureAwait(false);
                    _ = checked(expected.MinorUnits + sale.AmountSettled.MinorUnits);
                }
                catch (OverflowException) { throw new FoundationException("BillingAmountTooLarge"); }
            }
            var number = await store.NextReceiptAsync(c.Shop.Id, c.Terminal.Id, ct).ConfigureAwait(false);
            sale.Complete(number, clock.UtcNow);
            var movements = StockPosting.Movements(c.Shop.Id, "Sale", sale.Id.Value, sale.ItemsSold(), StockDirection.Out,
                StockMovementKind.Sale, clock.UtcNow, actor.Id, c.Shop.Currency,
                quote.NeedsStockApproval ? request.NegativeReason! : "Sale", quote.NeedsStockApproval ? owner : null);
            await store.PostStockAsync(c.Shop.Id, "Sale", sale.Id.Value, movements, ct).ConfigureAwait(false);
            store.Add(sale);
            store.AddReceipt(c.Shop.Id, RetailCore.Application.Receipts.ReceiptDocument.Capture(sale, c.Shop, actor.DisplayName));
            foreach (var line in sale.Lines.Where(l => l.ManualPriceApprovedBy is not null))
                store.Audit(AuditEntry.Record(c.Shop.Id, AuditAction.PriceOverridden, "Sale", sale.Id.Value, clock.UtcNow, actor.Id, owner,
                    detailsJson: JsonSerializer.Serialize(new { line.ProductId, line.Quantity, line.UnitPrice, line.QualifiedTierUnitPriceMinor,
                        line.QualifiedPriceBand, line.ManualPriceReason })));
            if (quote.NeedsStockApproval)
                store.Audit(AuditEntry.Record(c.Shop.Id, AuditAction.NegativeStockApproved, "Sale", sale.Id.Value, clock.UtcNow, actor.Id, owner,
                    detailsJson: JsonSerializer.Serialize(new { request.NegativeReason, quote.Stock })));
            var result = new CompletionResult(sale.Id.Value, number.ToString(), sale.Total.MinorUnits, sale.CashRoundingAdjustment.MinorUnits,
                sale.AmountSettled.MinorUnits, tendered.MinorUnits, sale.ChangeGiven.MinorUnits, sale.CurrencyCode, request.Method);
            store.Audit(AuditEntry.Record(c.Shop.Id, AuditAction.SaleCompleted, "Sale", sale.Id.Value, clock.UtcNow, actor.Id,
                detailsJson: JsonSerializer.Serialize(new { result.Receipt, result.TotalMinor, result.PaidMinor, result.Method })));
            store.CompleteCommand(request.BasketId, "CompleteSale", fingerprint, JsonSerializer.Serialize(result));
            store.SaveBasket(c.Shop.Id, c.Terminal.Id, actor.Id, BasketDocument.Empty(request, result));
            var current = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            if (current.Id != actor.Id || session.Version != sessionVersion) throw new FoundationException("SignInRequired");
            return result;
        }, cancellationToken);
    }

    internal async Task<BillingState> WithinAsync(ILocalStore store, CancellationToken ct) =>
        await StateAsync(store,await ContextAsync(store,ct).ConfigureAwait(false),ct).ConfigureAwait(false);
    internal async Task<Sale> BuildWithinAsync(ILocalStore store, UserId owner, CancellationToken ct) =>
        BuildSale(await ContextAsync(store,ct).ConfigureAwait(false),owner);

    private async Task<Context> ContextAsync(ILocalStore store, CancellationToken ct)
    {
        var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
        var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
        var terminal = (await store.TerminalsAsync(shop.Id, ct).ConfigureAwait(false)).Single(t => t.IsActive);
        var cart = await store.BasketAsync(shop.Id, terminal.Id, actor.Id, ct).ConfigureAwait(false);
        if (cart is null) { cart = BasketDocument.Empty(); store.SaveBasket(shop.Id, terminal.Id, actor.Id, cart); }
        var shift = await store.OpenShiftAsync(shop.Id, terminal.Id, ct).ConfigureAwait(false);
        if (shift is not null && shift.Currency != shop.Currency) throw new FoundationException("LocalConfiguration");
        return new Context(actor, shop, terminal, shift, cart,
            await store.ProductsAsync(shop.Id, ct).ConfigureAwait(false), await store.TaxRatesAsync(shop.Id, ct).ConfigureAwait(false),
            await store.StockAsync(shop.Id, ct).ConfigureAwait(false), await store.MovementsAsync(shop.Id, ct).ConfigureAwait(false));
    }

    private async Task<BillingState> SaveAsync(ILocalStore store, Context c, List<BasketItem> items, CancellationToken ct)
    {
        if (items.Count > 200) throw new FoundationException("BasketTooLarge");
        var cart = c.Basket with { Version = checked(c.Basket.Version + 1), Items = items.ToArray() };
        store.SaveBasket(c.Shop.Id, c.Terminal.Id, c.Actor.Id, cart);
        return await StateAsync(store, c with { Basket = cart }, ct).ConfigureAwait(false);
    }

    private async Task<BillingState> StateAsync(ILocalStore store, Context c, CancellationToken ct)
    {
        BillingQuote quote;
        try { quote = Quote(c); }
        catch (FoundationException ex)
        {
            var fallback = c.Basket.Items.Select(i =>
            {
                var p = c.Products.SingleOrDefault(p => p.Id == i.ProductId);
                return new BillingLine(i.ProductId, p?.Sku ?? "", p?.SinhalaName ?? "", p?.Name ?? "", p?.ReceiptName ?? "",
                    i.QuantityThousandths / 1000m, p?.Unit ?? UnitOfMeasure.Each, p?.UnitPrice.ToDecimal() ?? 0,
                    0, 0, 0, 0, 0, i.RequestedUnitPriceMinor is not null, i.Reason, 0, "", "", 0,
                    p?.StockBaseProductId ?? i.ProductId, p?.StockFactorThousandths ?? 1000, p?.StockBaseUnit ?? p?.Unit ?? UnitOfMeasure.Each);
            }).ToArray();
            quote = new BillingQuote("", fallback, [], 0, 0, 0, c.Shop.CashIncrementMinor, c.Shop.Currency.Code, false, ex.Key);
        }
        var categories = await store.CategoriesAsync(c.Shop.Id, ct).ConfigureAwait(false);
        return new BillingState(c.Basket, quote, c.Shift is null ? null : new BillingShift(c.Shift.Id, c.Shift.OpeningFloat.ToDecimal(),
            (await store.ExpectedCashAsync(c.Shift, ct).ConfigureAwait(false)).ToDecimal()),
            c.Products.Select(p => new BillingProduct(p.Id, p.Sku, p.SinhalaName, p.Name, p.CategoryId, p.Unit,
                p.Barcodes.Select(b => b.Code).ToArray(), p.IsActive && !p.IsOpenPrice && !string.IsNullOrWhiteSpace(p.ReceiptName))).ToArray(),
            categories.Where(c => c.IsActive).Select(c => new BillingCategory(c.Id, $"{c.SinhalaName} / {c.Name}")).ToArray());
    }

    private BillingQuote Quote(Context c)
    {
        var sale = BuildSale(c, c.Actor.Id); // Proposed discounts only; this aggregate is never persisted.
        var lines = sale.Lines.Select(l =>
        {
            var p = c.Products.Single(p => p.Id == l.ProductId);
            var tier = QuantityPricing.Preview(p, l.Quantity);
            return new BillingLine(l.ProductId, l.Sku, l.SinhalaNameSnapshot!, l.EnglishNameSnapshot!, l.ReceiptNameSnapshot!,
                l.Quantity.Value, l.Unit, p.UnitPrice.ToDecimal(), tier.UnitPrice.ToDecimal(), tier.Band + 1, l.UnitPrice.ToDecimal(),
                l.LineTotal.ToDecimal(), (p.UnitPrice.Extend(l.Quantity) - l.LineTotal).ToDecimal(), l.ManualPriceReason is not null,
                l.ManualPriceReason, l.UnitCost.MinorUnits, l.TaxTreatment.Code, l.TaxTreatment.Category.ToString(), l.TaxTreatment.Rate,
                l.StockProductId!.Value, l.StockFactorThousandths, l.StockBaseUnit!.Value);
        }).ToArray();
        var stock = sale.Lines.GroupBy(l => l.StockProductId!.Value).Select(g =>
        {
            var required = g.Sum(l => StockConversion.ToBase(l.Quantity, l.StockFactorThousandths).Thousandths);
            var available = c.Balances.GetValueOrDefault(g.Key);
            return new BillingStock(g.Key, available / 1000m, required / 1000m, checked(available - required) / 1000m,
                StockService.Revision(c.Movements, g.Key));
        }).OrderBy(s => s.ProductId.Value).ToArray();
        Money cash;
        try { cash = sale.Total.RoundToIncrement(c.Shop.CashIncrementMinor); }
        catch (OverflowException) { throw new FoundationException("BillingAmountTooLarge"); }
        var hash = Hash(new { c.Basket.Id, c.Basket.Version, Shift = c.Shift?.Id, Currency = c.Shop.Currency.Code,
            Increment = c.Shop.CashIncrementMinor, Lines = lines, Stock = stock });
        return new BillingQuote(hash, lines, stock, sale.Total.MinorUnits, cash.MinorUnits - sale.Total.MinorUnits,
            cash.MinorUnits, c.Shop.CashIncrementMinor, c.Shop.Currency.Code, lines.Any(l => l.Manual));
    }

    private Sale BuildSale(Context c, UserId? owner)
    {
        var sale = Sale.Start(c.Shop.Id, c.Terminal.Id, c.Shift?.Id ?? default, c.Actor.Id, c.Shop.Currency,
            clock.UtcNow, DiscountPolicy.AlwaysRequireApproval, new SaleId(c.Basket.Id));
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(c.Shop.TimeZoneId)).DateTime);
        try
        {
            foreach (var item in c.Basket.Items)
            {
                var p = c.Products.SingleOrDefault(p => p.Id == item.ProductId && p.IsActive && !p.IsOpenPrice)
                    ?? throw new FoundationException("BillingProductUnavailable");
                if (p.UnitPrice.Currency != c.Shop.Currency || p.UnitCost.Currency != c.Shop.Currency) throw new FoundationException("BillingProductUnavailable");
                ValidateItem(p, item);
                var root = c.Products.SingleOrDefault(r => r.Id == (p.StockBaseProductId ?? p.Id) && r.StockBaseProductId is null);
                if (root is null || root.Unit != (p.StockBaseUnit ?? p.Unit)) throw new FoundationException("InvalidStockConfiguration");
                var tax = c.Rates.SingleOrDefault(t => t.Id == p.TaxRateId && t.IsEffectiveOn(date))
                    ?? throw new FoundationException("BillingTaxUnavailable");
                sale.AddCheckoutLine(p, Quantity.FromThousandths(item.QuantityThousandths), tax.ToTreatment(),
                    item.RequestedUnitPriceMinor is { } price ? Money.FromMinor(price, c.Shop.Currency) : null, item.Reason, owner);
            }
        }
        catch (DomainException ex) { throw new FoundationException(ex.Message == "InvalidManualPrice" ? "InvalidManualPrice" : "BillingProductUnavailable"); }
        catch (OverflowException) { throw new FoundationException("InvalidBillingQuantity"); }
        return sale;
    }

    private static Quantity ParseQuantity(decimal value, UnitOfMeasure unit)
    {
        try
        {
            var quantity = Quantity.Of(value);
            if (!quantity.IsPositive || (!unit.AllowsFractionalQuantity() && !quantity.IsWholeUnits)) throw new FoundationException("InvalidBillingQuantity");
            return quantity;
        }
        catch (DomainException) { throw new FoundationException("InvalidBillingQuantity"); }
        catch (OverflowException) { throw new FoundationException("InvalidBillingQuantity"); }
    }
    private static void ValidateItem(Product p, BasketItem item)
    {
        var quantity = ParseQuantity(item.QuantityThousandths / 1000m, p.Unit);
        if (string.IsNullOrWhiteSpace(p.ReceiptName) || p.IsOpenPrice) throw new FoundationException("BillingProductUnavailable");
        try
        {
            var quote = QuantityPricing.Preview(p, quantity);
            if (item.RequestedUnitPriceMinor is { } final && (final <= 0 || final > quote.UnitPrice.MinorUnits
                || string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 500)) throw new FoundationException("InvalidManualPrice");
        }
        catch (DomainException) { throw new FoundationException("BillingProductUnavailable"); }
        catch (OverflowException) { throw new FoundationException("BillingAmountTooLarge"); }
    }
    private static void RequireVersion(BasketDocument basket, Guid id, long version)
    {
        if (id != basket.Id || version != basket.Version) throw new FoundationException("StaleBasket");
    }
    private static void ValidatePaymentInput(CompletionRequest request)
    {
        if (request.BasketId == Guid.Empty || string.IsNullOrEmpty(request.QuoteHash) || request.QuoteHash.Length != 64 || request.TenderedMinor < 0
            || request.Method is not (PaymentMethod.Cash or PaymentMethod.Card) || request.NegativeReason?.Length > 500)
            throw new FoundationException("InvalidTender");
        if (request.Method == PaymentMethod.Card && !request.CardApproved) throw new FoundationException("CardApprovalRequired");
        if (request.Method == PaymentMethod.Cash && (request.CardApproved || !string.IsNullOrEmpty(request.CardReference)))
            throw new FoundationException("InvalidCardReference");
        if (!ExternalCardReference.IsValid(request.CardReference)) throw new FoundationException("InvalidCardReference");
    }
}
