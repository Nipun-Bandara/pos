using RetailCore.Domain.Common;
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
        RefundId? id = null)
    {
        ArgumentNullException.ThrowIfNull(originalSale);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (originalSale.Status != SaleStatus.Completed)
        {
            throw new DomainException(
                $"Receipt {originalSale.ReceiptNumber} is {originalSale.Status}, so it cannot be refunded.");
        }

        return new Refund(
            id ?? RefundId.New(),
            originalSale,
            shiftId,
            cashierId,
            reason.Trim(),
            authorisedBy,
            method,
            createdAt,
            alreadyRefunded ?? new Dictionary<SaleLineId, Quantity>());
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

        var amount = AmountFor(originalLine, quantity, remaining, previouslyRefunded);
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
            [.. _lines.Select(l => new SoldItem(l.ProductId, l.Quantity, l.StockProductId, l.StockFactorThousandths,
                l.StockBaseUnit ?? RetailCore.Domain.Catalogue.UnitOfMeasure.Each,
                l.CostCurrency is null ? null : Money.FromMinor(l.UnitCostMinor, Monetary.Currency.FromCode(l.CostCurrency)),
                l.Disposition == ReturnDisposition.Saleable))],
            at));
    }

    /// <summary>
    /// What to give back for part of a line.
    /// </summary>
    /// <remarks>
    /// Pro-rated from the amount actually charged, so discounts carry through. The final
    /// outstanding quantity gets whatever is left rather than its own rounded share, which
    /// guarantees that refunding a line in pieces returns exactly what was paid for it and
    /// never a minor unit more or less. RET-03.
    /// </remarks>
    private static Money AmountFor(
        SaleLine originalLine,
        Quantity quantity,
        Quantity remaining,
        Quantity previouslyRefunded)
    {
        var lineTotal = originalLine.LineTotal;

        if (quantity == remaining)
        {
            // Refunding everything that is left: hand back the charge less what has gone already.
            var alreadyGivenBack = previouslyRefunded.IsZero
                ? Money.Zero(lineTotal.Currency)
                : lineTotal.MultiplyBy(previouslyRefunded.Value / originalLine.Quantity.Value);

            return lineTotal - alreadyGivenBack;
        }

        return lineTotal.MultiplyBy(quantity.Value / originalLine.Quantity.Value);
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
