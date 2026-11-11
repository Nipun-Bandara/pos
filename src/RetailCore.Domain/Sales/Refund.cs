using RetailCore.Domain.Common;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Sales;

/// <summary>
/// Money given back against an earlier sale. RET-01.
/// </summary>
/// <remarks>
/// <para>
/// A refund is its own aggregate rather than a mutation of the sale. CMP-04 makes financial
/// records immutable, and a shop may refund the same sale more than once over several visits,
/// so the sale cannot carry a single "refunded" flag.
/// </para>
/// <para>
/// RET-03 is enforced at construction: the caller supplies what has already been refunded
/// against each line, and the aggregate refuses a quantity that would take the total past
/// what was sold. Doing that check here rather than in a service is what makes it impossible
/// to bypass by using a different code path.
/// </para>
/// </remarks>
public sealed class Refund : AggregateRoot<RefundId>, IBelongsToShop
{
    private readonly List<RefundLine> _lines = [];
    private readonly Sale? _originalSale;
    private readonly Dictionary<SaleLineId, Quantity> _alreadyRefunded = [];
    private readonly Dictionary<SaleLineId, long> _alreadyAmounts = [];
    public long? CashCreditMinor { get; private set; }
    public long? CardCreditMinor { get; private set; }
    public long? CashPaidMinor { get; private set; }
    public long? CardPaidMinor { get; private set; }
    public long ExchangeCreditMinor { get; private set; }
    public SaleId? ReplacementSaleId { get; private set; }
    public bool IsVoidReversal { get; private set; }
    public string? ExternalCardReference { get; private set; }

    public void SetSettlement(long cashCredit, long cardCredit, long cashPaid, long cardPaid, SaleId? replacement, string? externalReference)
    {
        RequireOpen();
        if (cashCredit < 0 || cardCredit < 0 || cashPaid < 0 || cardPaid < 0 || cashPaid > cashCredit || cardPaid > cardCredit
            || checked(cashCredit+cardCredit) != Total.MinorUnits || (checked(cashPaid+cardPaid)<Total.MinorUnits && replacement is null) || !global::RetailCore.Domain.Sales.ExternalCardReference.IsValid(externalReference))
            throw new DomainException("InvalidReturnSettlement");
        Method=cashCredit==0 && cardCredit>0?PaymentMethod.Card:PaymentMethod.Cash;
        CashCreditMinor=cashCredit; CardCreditMinor=cardCredit; CashPaidMinor=cashPaid; CardPaidMinor=cardPaid;
        ExchangeCreditMinor=checked(Total.MinorUnits-cashPaid-cardPaid); ReplacementSaleId=replacement; ExternalCardReference=externalReference;
    }

    private Refund(
        RefundId id,
        Sale originalSale,
        ShiftId shiftId,
        UserId cashierId,
        string reason,
        UserId authorisedBy,
        PaymentMethod method,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<SaleLineId, Quantity> alreadyRefunded)
        : base(id)
    {
        _originalSale = originalSale;
        OriginalSaleId = originalSale.Id;
        ShopId = originalSale.ShopId;
        TerminalId = originalSale.TerminalId;
        ShiftId = shiftId;
        CashierId = cashierId;
        CurrencyCode = originalSale.CurrencyCode;
        Reason = reason;
        AuthorisedBy = authorisedBy;
        Method = method;
        CreatedAt = createdAt;

        foreach (var (lineId, quantity) in alreadyRefunded)
        {
            _alreadyRefunded[lineId] = quantity;
        }

        Total = Money.Zero(originalSale.Currency);
        TaxTotal = Money.Zero(originalSale.Currency);
    }

    private Refund()
    {
        CurrencyCode = null!;
        Reason = null!;
    }

    public SaleId OriginalSaleId { get; private set; }

    public ShopId ShopId { get; private set; }

    public TerminalId TerminalId { get; private set; }

    /// <summary>The shift the refund is paid out of, so the Z report accounts for it. CSH-04.</summary>
    public ShiftId ShiftId { get; private set; }

    public UserId CashierId { get; private set; }

    public string CurrencyCode { get; private set; }

    public Currency Currency => Monetary.Currency.FromCode(CurrencyCode);

    /// <summary>Mandatory. RET-04 applies the same standard to refunds.</summary>
    public string Reason { get; private set; }

    /// <summary>RET-05: a refund always needs a manager or owner.</summary>
    public UserId AuthorisedBy { get; private set; }

    /// <summary>How the money went back. Normally the method the customer paid with.</summary>
    public PaymentMethod Method { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Assigned at completion from the terminal's counter, as for a sale. SYN-03.</summary>
    public ReceiptNumber? ReceiptNumber { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public IReadOnlyList<RefundLine> Lines => _lines;

    /// <summary>Total being returned, tax inclusive.</summary>
    public Money Total { get; private set; }

    public Money TaxTotal { get; private set; }

    /// <summary>
    /// Starts a refund against a completed sale.
    /// </summary>
    /// <param name="originalSale">The sale being refunded. Must be completed and not voided.</param>
    /// <param name="alreadyRefunded">
    /// Quantities already returned against each line by earlier refunds. Supplied by the
    /// caller because it spans aggregates; the check itself belongs here. RET-03.
    /// </param>
    public static Refund Against(
        Sale originalSale,
        ShiftId shiftId,
        UserId cashierId,
        string reason,
        UserId authorisedBy,
        PaymentMethod method,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<SaleLineId, Quantity>? alreadyRefunded = null,
        RefundId? id = null, IReadOnlyDictionary<SaleLineId, long>? alreadyRefundedAmounts = null)
    {
        ArgumentNullException.ThrowIfNull(originalSale);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (originalSale.Status != SaleStatus.Completed)
        {
            throw new DomainException(
                $"Receipt {originalSale.ReceiptNumber} is {originalSale.Status}, so it cannot be refunded.");
        }

        var refund = new Refund(
            id ?? RefundId.New(),
            originalSale,
            shiftId,
            cashierId,
            reason.Trim(),
            authorisedBy,
            method,
            createdAt,
            alreadyRefunded ?? new Dictionary<SaleLineId, Quantity>());
        var entitlements = ReturnAmounts.Entitlements(originalSale);
        foreach (var (line, quantity) in refund._alreadyRefunded)
        {
            if (quantity.Thousandths < 0 || !entitlements.ContainsKey(line)) throw new DomainException("InvalidReturnHistory");
            if (quantity.IsPositive && (alreadyRefundedAmounts is null || !alreadyRefundedAmounts.ContainsKey(line)))
                throw new DomainException("ActualRefundAmountsRequired");
        }
        foreach (var (line, amount) in alreadyRefundedAmounts ?? new Dictionary<SaleLineId,long>())
        {
            if (amount < 0 || !entitlements.TryGetValue(line,out var cap) || amount > cap) throw new DomainException("InvalidReturnHistory");
            refund._alreadyAmounts[line]=amount;
        }
        return refund;
    }

    /// <summary>
    /// Adds a line to give back. RET-02 for a partial return, or the full quantity for a
    /// whole-line return.
    /// </summary>
    /// <exception cref="DomainException">
    /// RET-03: the quantity would take the refunded total past what was sold.
    /// </exception>
    public RefundLine AddLine(SaleLineId originalLineId, Quantity quantity, ReturnDisposition disposition = ReturnDisposition.Saleable)
    {
        RequireOpen();

        var sale = _originalSale
            ?? throw new DomainException("The original sale must be loaded to add refund lines.");

        var originalLine = sale.Lines.FirstOrDefault(l => l.Id == originalLineId)
            ?? throw new DomainException("That line is not on the original sale.");

        if (originalLine.IsVoided)
        {
            throw new DomainException(
                $"'{originalLine.Description}' was voided on the original sale, so there is nothing to refund.");
        }

        if (!quantity.IsPositive)
        {
            throw new DomainException("A refund needs a quantity greater than zero.");
        }

        if (_lines.Any(l => l.OriginalSaleLineId == originalLineId))
        {
            throw new DomainException(
                $"'{originalLine.Description}' is already on this refund. Change that line instead.");
        }

        var previouslyRefunded = _alreadyRefunded.GetValueOrDefault(originalLineId, Quantity.Zero);
        var remaining = originalLine.Quantity - previouslyRefunded;

        if (quantity > remaining)
        {
            throw new DomainException(
                $"Only {remaining} of '{originalLine.Description}' is left to refund, but {quantity} was asked for.");
        }

        if (!originalLine.Unit.AllowsFractionalQuantity() && !quantity.IsWholeUnits) throw new DomainException("InvalidReturnQuantity");
        var cap = ReturnAmounts.Entitlements(sale)[originalLine.Id];
        var left = checked(cap - _alreadyAmounts.GetValueOrDefault(originalLineId));
        var amount = Money.FromMinor(quantity == remaining ? left : Math.Min(left,ReturnAmounts.Share(cap,quantity.Thousandths,originalLine.Quantity.Thousandths)),sale.Currency);
        var line = RefundLine.Create(Id, ShopId, originalLine, quantity, amount, disposition: disposition);

        _lines.Add(line);
        Recalculate();
        return line;
    }

    public void RemoveLine(RefundLineId lineId)
    {
        RequireOpen();

        var removed = _lines.RemoveAll(l => l.Id == lineId);
        if (removed == 0)
        {
            throw new DomainException("That line is not on this refund.");
        }

        Recalculate();
    }

    /// <summary>Closes the refund and raises the event that restores stock. RET-06.</summary>
    public void Complete(ReceiptNumber receiptNumber, DateTimeOffset at)
    {
        RequireOpen();

        if (_lines.Count == 0)
        {
            throw new DomainException("There is nothing on this refund.");
        }

        if (CashCreditMinor is { } cash && (checked(cash+CardCreditMinor!.Value)!=Total.MinorUnits
            || checked(CashPaidMinor!.Value+CardPaidMinor!.Value+ExchangeCreditMinor)!=Total.MinorUnits)) throw new DomainException("InvalidReturnSettlement");
        ReceiptNumber = receiptNumber;
        CompletedAt = at;

        Raise(new RefundIssued(
            Id,
            OriginalSaleId,
            ShopId,
            TerminalId,
            ShiftId,
            CashierId,
            Total,
            Reason,
            AuthorisedBy,
            IsVoidReversal ? [] : [.. _lines.Select(l => new SoldItem(l.ProductId, l.Quantity, l.StockProductId, l.StockFactorThousandths,
                l.StockBaseUnit ?? RetailCore.Domain.Catalogue.UnitOfMeasure.Each,
                l.CostCurrency is null ? null : Money.FromMinor(l.UnitCostMinor, Monetary.Currency.FromCode(l.CostCurrency)),
                l.Disposition == ReturnDisposition.Saleable))],
            at));
    }

    public void CompleteVoidReversal(Sale original, ReceiptNumber receiptNumber, DateTimeOffset at)
    {
        if (original.Id != OriginalSaleId || original.Status != SaleStatus.Voided || Total != original.AmountSettled
            || _alreadyRefunded.Values.Any(q => q.IsPositive)
            || _lines.Count != original.ActiveLines.Count() || _lines.Any(l => l.Quantity != original.Lines.Single(o => o.Id == l.OriginalSaleLineId).Quantity))
            throw new DomainException("VoidNotEligible");
        IsVoidReversal=true; Complete(receiptNumber,at);
    }

    private void Recalculate()
    {
        var currency = Currency;
        Total = Money.Sum(_lines.Select(l => l.Amount), currency);
        TaxTotal = Money.Sum(_lines.Select(l => l.TaxAmount), currency);
    }

    private void RequireOpen()
    {
        if (IsCompleted)
        {
            throw new DomainException("This refund has been completed and can no longer be changed.");
        }
    }
}
