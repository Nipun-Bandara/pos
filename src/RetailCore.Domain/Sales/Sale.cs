using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Sales;

/// <summary>
/// A basket being rung up, and once completed, the immutable record of a transaction.
/// </summary>
/// <remarks>
/// <para>
/// SAL-10 is the rule that shapes this class: totals are derived from the lines and there
/// is no way to assign one. Every property that looks like a total has a private setter and
/// is written only by <see cref="Recalculate"/>. They are stored rather than computed on
/// read so that the back office can sum them in SQL, but the only writer is the aggregate.
/// </para>
/// <para>
/// The sale is the transactional boundary. Lines and payments are only reachable through it,
/// which is what lets PAY-06 (a short payment cannot complete a sale) be an invariant rather
/// than a check somebody remembered to write in the UI.
/// </para>
/// </remarks>
public sealed class Sale : AggregateRoot<SaleId>, IBelongsToShop
{
    private readonly List<SaleLine> _lines = [];
    private readonly List<Payment> _payments = [];

    private Sale(
        SaleId id,
        ShopId shopId,
        TerminalId terminalId,
        ShiftId shiftId,
        UserId cashierId,
        Currency currency,
        DiscountPolicy discountPolicy,
        DateTimeOffset startedAt)
        : base(id)
    {
        ShopId = shopId;
        TerminalId = terminalId;
        ShiftId = shiftId;
        CashierId = cashierId;
        CurrencyCode = currency.Code;
        DiscountPolicy = discountPolicy;
        StartedAt = startedAt;
        Status = SaleStatus.Open;

        var zero = Money.Zero(currency);
        GrossAmount = zero;
        DiscountTotal = zero;
        TaxTotal = zero;
        NetTotal = zero;
        Total = zero;
        CashRoundingAdjustment = zero;
    }

    private Sale()
    {
        CurrencyCode = null!;
        DiscountPolicy = Sales.DiscountPolicy.Default;
    }

    /// <summary>TEN-01.</summary>
    public ShopId ShopId { get; private set; }

    public TerminalId TerminalId { get; private set; }

    /// <summary>Every sale belongs to a shift, so the Z report can account for it. CSH-04.</summary>
    public ShiftId ShiftId { get; private set; }

    public UserId CashierId { get; private set; }

    /// <summary>Persisted as the ISO code; <see cref="Currency"/> is the domain view of it.</summary>
    public string CurrencyCode { get; private set; }

    public Currency Currency => Monetary.Currency.FromCode(CurrencyCode);

    public SaleStatus Status { get; private set; }

    /// <summary>
    /// Assigned at completion, not at creation. SYN-03 requires the sequence to be gapless,
    /// so an abandoned basket must not consume a number.
    /// </summary>
    public ReceiptNumber? ReceiptNumber { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>RET-04. Mandatory when the sale is voided.</summary>
    public string? VoidReason { get; private set; }

    public UserId? VoidedBy { get; private set; }

    /// <summary>SAL-14.</summary>
    public string? CustomerName { get; private set; }

    /// <summary>SAL-14.</summary>
    public string? CustomerPhone { get; private set; }

    /// <summary>SAL-08. Applied to the basket as a whole and apportioned across the lines.</summary>
    public Discount? SaleDiscount { get; private set; }

    /// <summary>SAL-09. Snapshotted so a later change to shop settings cannot rewrite history.</summary>
    public DiscountPolicy DiscountPolicy { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _lines;

    public IReadOnlyList<Payment> Payments => _payments;

    /// <summary>Lines that still count. Voided lines stay in <see cref="Lines"/> for the audit trail.</summary>
    public IEnumerable<SaleLine> ActiveLines => _lines.Where(l => !l.IsVoided);

    /// <summary>Sum of price times quantity across active lines, before any discount.</summary>
    public Money GrossAmount { get; private set; }

    /// <summary>Line discounts plus the whole-sale discount. SAL-07, SAL-08.</summary>
    public Money DiscountTotal { get; private set; }

    /// <summary>Tax contained within <see cref="Total"/>. CMP-01.</summary>
    public Money TaxTotal { get; private set; }

    /// <summary>Total less tax.</summary>
    public Money NetTotal { get; private set; }

    /// <summary>What the customer owes, tax inclusive, before cash rounding.</summary>
    public Money Total { get; private set; }

    /// <summary>
    /// The difference cash rounding makes to this sale. PAY-02.
    /// </summary>
    /// <remarks>
    /// Positive when rounding up in the shop's favour, negative when down. Held separately
    /// from the total so the Z report can show it as its own line: it is neither income nor
    /// a discount, and burying it in either makes the drawer reconciliation unexplainable.
    /// </remarks>
    public Money CashRoundingAdjustment { get; private set; }

    /// <summary>What the customer actually has to hand over. PAY-02.</summary>
    public Money AmountPayable => Total + CashRoundingAdjustment;

    /// <summary>Sum of what the tenders settle, ignoring change.</summary>
    public Money AmountSettled => Money.Sum(_payments.Select(p => p.Amount), Currency);

    /// <summary>What is still owed. Zero or less means the sale can complete. PAY-06.</summary>
    public Money BalanceDue => AmountPayable - AmountSettled;

    /// <summary>Change handed back across all tenders. PAY-01.</summary>
    public Money ChangeGiven => Money.Sum(_payments.Select(p => p.Change), Currency);

    /// <summary>Cost of goods sold, for the margin report. BO-08.</summary>
    public Money CostOfGoods => Money.Sum(ActiveLines.Select(l => l.CostAmount), Currency);

    public bool IsSettled => !BalanceDue.IsPositive;

    public bool IsEditable => Status is SaleStatus.Open or SaleStatus.Held;

    public static Sale Start(
        ShopId shopId,
        TerminalId terminalId,
        ShiftId shiftId,
        UserId cashierId,
        Currency currency,
        DateTimeOffset startedAt,
        DiscountPolicy? discountPolicy = null,
        SaleId? id = null)
    {
        if (!currency.IsSpecified)
        {
            throw new DomainException("A sale must have a currency. It comes from the shop.");
        }

        return new Sale(
            id ?? SaleId.New(),
            shopId,
            terminalId,
            shiftId,
            cashierId,
            currency,
            discountPolicy ?? DiscountPolicy.Default,
            startedAt);
    }

    /// <summary>
    /// Adds a product to the basket, snapshotting its price, cost and tax treatment.
    /// SAL-01, SAL-02, SAL-03.
    /// </summary>
    /// <param name="product">The catalogue entry being sold.</param>
    /// <param name="quantity">How much. SAL-04, SAL-05.</param>
    /// <param name="taxTreatment">The tax rule effective on the date of sale. CMP-02.</param>
    /// <param name="unitPriceOverride">
    /// Set for an open-price product (SAL-13) or an authorised price override.
    /// </param>
    public SaleLine AddLine(
        Product product,
        Quantity quantity,
        TaxTreatment taxTreatment,
        Money? unitPriceOverride = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(taxTreatment);
        RequireEditable();
        RequireNoPayments("adding an item");

        if (product.ShopId != ShopId)
        {
            throw new DomainException(
                $"'{product.Name}' belongs to another shop and cannot be sold here.");
        }

        var unitPrice = unitPriceOverride ?? product.UnitPrice;

        if (product.IsOpenPrice && unitPriceOverride is null)
        {
            throw new DomainException($"'{product.Name}' is an open-price item, so it needs a price.");
        }

        if (unitPrice.Currency != Currency)
        {
            throw new DomainException(
                $"'{product.Name}' is priced in {unitPrice.Currency.Code} but this sale is in {CurrencyCode}.");
        }

        var line = SaleLine.Create(
            Id,
            ShopId,
            NextLineNumber(),
            product.Id,
            product.Sku,
            product.Name,
            product.Unit,
            unitPrice,
            product.UnitCost,
            quantity,
            taxTreatment);

        _lines.Add(line);
        Recalculate();
        return line;
    }

    /// <summary>SAL-04.</summary>
    public void ChangeLineQuantity(SaleLineId lineId, Quantity quantity)
    {
        RequireEditable();
        RequireNoPayments("changing a quantity");

        var line = RequireLine(lineId);
        if (line.IsVoided)
        {
            throw new DomainException($"'{line.Description}' has been voided and cannot be changed.");
        }

        line.ChangeQuantity(quantity);
        Recalculate();
    }

    /// <summary>SAL-06. The line stays on the record, marked voided, with its reason.</summary>
    public void VoidLine(SaleLineId lineId, string reason, UserId? voidedBy, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireEditable();
        RequireNoPayments("voiding a line");

        var line = RequireLine(lineId);
        if (line.IsVoided)
        {
            return;
        }

        var amount = line.LineTotal;
        line.Void(reason, voidedBy);
        Recalculate();

        Raise(new SaleLineVoided(Id, ShopId, lineId, line.Description, amount, reason.Trim(), voidedBy, at));
    }

    /// <summary>SAL-07, subject to SAL-09.</summary>
    public void ApplyLineDiscount(SaleLineId lineId, Discount discount, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(discount);
        RequireEditable();
        RequireNoPayments("changing a discount");

        var line = RequireLine(lineId);
        if (line.IsVoided)
        {
            throw new DomainException($"'{line.Description}' has been voided and cannot be discounted.");
        }

        var baseAmount = line.UnitPrice.Extend(line.Quantity);
        RequireSameCurrency(discount, baseAmount.Currency);
        DiscountPolicy.EnsureSatisfied(discount, baseAmount);

        line.ApplyDiscount(discount);
        Recalculate();

        Raise(new DiscountApplied(
            Id, ShopId, lineId, line.LineDiscountAmount, discount.Reason, discount.AuthorisedBy, at));
    }

    public void RemoveLineDiscount(SaleLineId lineId)
    {
        RequireEditable();
        RequireNoPayments("changing a discount");

        RequireLine(lineId).RemoveDiscount();
        Recalculate();
    }

    /// <summary>SAL-13 and an authorised price override.</summary>
    public void OverrideLinePrice(SaleLineId lineId, Money unitPrice)
    {
        RequireEditable();
        RequireNoPayments("changing a price");

        RequireLine(lineId).OverridePrice(unitPrice);
        Recalculate();
    }

    /// <summary>SAL-08. Apportioned across the lines so tax stays correct per line.</summary>
    public void ApplySaleDiscount(Discount discount, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(discount);
        RequireEditable();
        RequireNoPayments("changing a discount");

        var baseAmount = Money.Sum(
            ActiveLines.Select(l => l.GrossAmount - l.LineDiscountAmount),
            Currency);

        RequireSameCurrency(discount, Currency);
        DiscountPolicy.EnsureSatisfied(discount, baseAmount);

        SaleDiscount = discount;
        Recalculate();

        var amount = Money.Sum(ActiveLines.Select(l => l.ApportionedSaleDiscount), Currency);
        Raise(new DiscountApplied(Id, ShopId, null, amount, discount.Reason, discount.AuthorisedBy, at));
    }

    public void RemoveSaleDiscount()
    {
        RequireEditable();
        RequireNoPayments("changing a discount");

        SaleDiscount = null;
        Recalculate();
    }

    /// <summary>SAL-14.</summary>
    public void SetCustomer(string? name, string? phone)
    {
        RequireEditable();
        CustomerName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        CustomerPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    /// <summary>
    /// Takes cash. PAY-01 and PAY-02.
    /// </summary>
    /// <remarks>
    /// When the cash covers everything still owed, the outstanding amount is rounded to the
    /// smallest circulating coin and the difference is recorded as
    /// <see cref="CashRoundingAdjustment"/>. When the cash is only part of a split tender
    /// (PAY-05) no rounding happens, because the remainder will be settled exactly by card
    /// and rounding an intermediate step would leave a stray minor unit outstanding.
    /// </remarks>
    /// <param name="tendered">What the customer handed over.</param>
    /// <returns>The change to hand back, which is zero unless the cash over-covers the balance.</returns>
    public Money AddCashPayment(Money tendered, DateTimeOffset at)
    {
        RequireEditable();
        RequireCurrency(tendered);

        if (!tendered.IsPositive)
        {
            throw new DomainException("The cash tendered must be more than zero.");
        }

        var outstanding = BalanceDue;
        if (!outstanding.IsPositive)
        {
            throw new DomainException("This sale is already paid in full.");
        }

        var rounded = outstanding.RoundToCashIncrement();

        Money applied;
        if (tendered >= rounded)
        {
            // The cash settles the sale, so rounding applies and any excess is change.
            CashRoundingAdjustment += rounded - outstanding;
            applied = rounded;
        }
        else
        {
            // Part payment. The balance stays exact for the tender that follows.
            applied = tendered;
        }

        var payment = Payment.Create(Id, ShopId, PaymentMethod.Cash, applied, tendered, null, at);
        _payments.Add(payment);
        return payment.Change;
    }

    /// <summary>
    /// Takes a non-cash tender. PAY-03, PAY-04, PAY-05.
    /// </summary>
    /// <param name="reference">
    /// The approval code from the card machine or wallet. No card data is accepted here and
    /// none is stored. CMP-06.
    /// </param>
    public void AddPayment(PaymentMethod method, Money amount, string? reference, DateTimeOffset at)
    {
        RequireEditable();
        RequireCurrency(amount);

        if (method == PaymentMethod.Cash)
        {
            throw new DomainException("Use AddCashPayment for cash, so that rounding is applied.");
        }

        if (!amount.IsPositive)
        {
            throw new DomainException("A payment must be more than zero.");
        }

        var outstanding = BalanceDue;
        if (!outstanding.IsPositive)
        {
            throw new DomainException("This sale is already paid in full.");
        }

        if (amount > outstanding)
        {
            throw new DomainException(
                $"{method} cannot over-tender. The balance is {outstanding} but {amount} was offered.");
        }

        _payments.Add(Payment.Create(Id, ShopId, method, amount, amount, reference, at));
    }

    /// <summary>Removes the most recent tender, for the cashier who picked the wrong key.</summary>
    public void RemoveLastPayment()
    {
        RequireEditable();

        if (_payments.Count == 0)
        {
            throw new DomainException("There are no payments to remove.");
        }

        var last = _payments[^1];
        _payments.RemoveAt(_payments.Count - 1);

        // Rounding was a consequence of that tender, so it goes with it. Recomputed from
        // scratch rather than subtracted, because several cash tenders may have contributed.
        if (last.Method == PaymentMethod.Cash)
        {
            CashRoundingAdjustment = Money.Zero(Currency);
        }
    }

    /// <summary>SAL-11: park the basket so the next customer can be served.</summary>
    public void Hold()
    {
        if (Status != SaleStatus.Open)
        {
            throw new DomainException($"A {Status} sale cannot be held.");
        }

        if (_payments.Count > 0)
        {
            throw new DomainException("This sale has already been part paid, so it cannot be held.");
        }

        Status = SaleStatus.Held;
    }

    /// <summary>SAL-11.</summary>
    public void Recall()
    {
        if (Status != SaleStatus.Held)
        {
            throw new DomainException($"A {Status} sale cannot be recalled.");
        }

        Status = SaleStatus.Open;
    }

    /// <summary>Abandons a basket that was never paid for. No financial effect.</summary>
    public void Cancel()
    {
        RequireEditable();

        if (_payments.Count > 0)
        {
            throw new DomainException("Remove the payments before cancelling this sale.");
        }

        Status = SaleStatus.Cancelled;
    }

    /// <summary>
    /// Closes the sale. PAY-06.
    /// </summary>
    /// <param name="receiptNumber">
    /// Allocated by the terminal's gapless counter at commit time. SYN-03.
    /// </param>
    /// <exception cref="DomainException">
    /// The basket is empty, or the tenders do not cover the amount payable.
    /// </exception>
    public void Complete(ReceiptNumber receiptNumber, DateTimeOffset at)
    {
        RequireEditable();

        if (!ActiveLines.Any())
        {
            throw new DomainException("There is nothing on this sale to pay for.");
        }

        if (BalanceDue.IsPositive)
        {
            throw new DomainException(
                $"{BalanceDue} is still owed on this sale, so it cannot be completed.");
        }

        Status = SaleStatus.Completed;
        ReceiptNumber = receiptNumber;
        CompletedAt = at;

        Raise(new SaleCompleted(
            Id,
            ShopId,
            TerminalId,
            ShiftId,
            CashierId,
            receiptNumber,
            Total,
            ItemsSold(),
            at));
    }

    /// <summary>
    /// Reverses a completed sale in full. RET-04, RET-05.
    /// </summary>
    /// <remarks>
    /// CMP-04 keeps the original rows untouched. Voiding sets a status and raises an event
    /// that puts the stock back (RET-06); it never deletes or edits the sale.
    /// </remarks>
    public void Void(string reason, UserId authorisedBy, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status != SaleStatus.Completed)
        {
            throw new DomainException($"Only a completed sale can be voided, and this one is {Status}.");
        }

        Status = SaleStatus.Voided;
        VoidReason = reason.Trim();
        VoidedBy = authorisedBy;
        VoidedAt = at;

        Raise(new SaleVoided(
            Id,
            ShopId,
            TerminalId,
            ReceiptNumber!.Value,
            Total,
            reason.Trim(),
            authorisedBy,
            ItemsSold(),
            at));
    }

    /// <summary>What left the shop, for the stock ledger. INV-03.</summary>
    public IReadOnlyList<SoldItem> ItemsSold() =>
        [.. ActiveLines.Select(l => new SoldItem(l.ProductId, l.Quantity))];

    /// <summary>
    /// Rewrites every derived total from the lines. The only writer of the total properties.
    /// </summary>
    private void Recalculate()
    {
        var currency = Currency;
        var active = ActiveLines.ToList();

        // Apportion the whole-sale discount before reading any line total, because a line's
        // tax depends on its share of it.
        ApportionSaleDiscount(active, currency);

        GrossAmount = Money.Sum(active.Select(l => l.GrossAmount), currency);
        DiscountTotal = Money.Sum(active.Select(l => l.TotalDiscountAmount), currency);
        Total = Money.Sum(active.Select(l => l.LineTotal), currency);
        TaxTotal = Money.Sum(active.Select(l => l.TaxAmount), currency);
        NetTotal = Total - TaxTotal;
    }

    private void ApportionSaleDiscount(List<SaleLine> active, Currency currency)
    {
        var zero = Money.Zero(currency);

        if (SaleDiscount is null || active.Count == 0)
        {
            foreach (var line in active)
            {
                line.ApportionSaleDiscount(zero);
            }

            return;
        }

        var bases = active
            .Select(l => l.GrossAmount - l.LineDiscountAmount)
            .ToList();

        var total = Money.Sum(bases, currency);
        var discount = SaleDiscount.ComputeOn(total);

        if (!total.IsPositive || !discount.IsPositive)
        {
            foreach (var line in active)
            {
                line.ApportionSaleDiscount(zero);
            }

            return;
        }

        // Largest remainder, so the shares add back to the discount exactly and the sum of
        // the printed line totals equals the printed sale total.
        var shares = discount.Allocate([.. bases.Select(b => b.MinorUnits)]);

        for (var i = 0; i < active.Count; i++)
        {
            active[i].ApportionSaleDiscount(shares[i]);
        }
    }

    private int NextLineNumber() => _lines.Count == 0 ? 1 : _lines.Max(l => l.LineNumber) + 1;

    private SaleLine RequireLine(SaleLineId lineId) =>
        _lines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainException("That line is not on this sale.");

    private void RequireEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException($"This sale is {Status} and can no longer be changed.");
        }
    }

    private void RequireNoPayments(string action)
    {
        if (_payments.Count > 0)
        {
            throw new DomainException(
                $"Payment has already been taken. Remove the payments before {action}.");
        }
    }

    private void RequireCurrency(Money amount)
    {
        if (amount.Currency != Currency)
        {
            throw new DomainException(
                $"This sale is in {CurrencyCode} but {amount.Currency.Code} was offered.");
        }
    }

    private static void RequireSameCurrency(Discount discount, Currency currency)
    {
        if (discount.Kind == DiscountKind.FixedAmount && discount.FixedAmount.Currency != currency)
        {
            throw new DomainException(
                $"The discount is in {discount.FixedAmount.Currency.Code} but the sale is in {currency.Code}.");
        }
    }
}
