using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Shifts;

public enum ShiftStatus
{
    Open = 0,

    Closed = 1,
}

/// <summary>
/// What the sales system knows about the money side of a shift, supplied when the shift
/// is reported on or closed.
/// </summary>
/// <remarks>
/// Sales live in their own aggregate, so the shift cannot read them directly without making
/// the transaction span every sale of the day. The caller queries the totals and hands them
/// in; the arithmetic that turns them into an expected drawer figure stays here, where it
/// can be tested.
/// </remarks>
public sealed record ShiftSalesSummary(
    Money CashSales,
    Money CashRefunds,
    Money NonCashSales,
    Money NonCashRefunds,
    int SaleCount,
    int RefundCount,
    Money TaxCollected,
    Money DiscountsGiven,
    Money CashRounding);

/// <summary>
/// A period of trading on one terminal, from counting the float to counting the drawer.
/// </summary>
/// <remarks>
/// <para>
/// CSH-02 allows one open shift per terminal, enforced by a filtered unique index in the
/// database rather than by a check here, because two tills racing to open a shift is exactly
/// the case an in-memory check would miss.
/// </para>
/// <para>
/// PLAN.md calls the Z report matching the physical drawer a proof point for the whole money
/// model. That makes <see cref="Close"/> the most consequential method in this file: expected
/// cash is float plus cash taken, less cash refunded and cash removed, and the variance is
/// whatever the count does not explain.
/// </para>
/// </remarks>
public sealed class Shift : AggregateRoot<ShiftId>, IBelongsToShop
{
    private readonly List<CashMovement> _cashMovements = [];

    private Shift(
        ShiftId id,
        ShopId shopId,
        TerminalId terminalId,
        UserId openedBy,
        Money openingFloat,
        DateTimeOffset openedAt)
        : base(id)
    {
        ShopId = shopId;
        TerminalId = terminalId;
        OpenedBy = openedBy;
        OpeningFloat = openingFloat;
        OpenedAt = openedAt;
        Status = ShiftStatus.Open;
        CurrencyCode = openingFloat.Currency.Code;
    }

    private Shift() => CurrencyCode = null!;

    public ShopId ShopId { get; private set; }

    public TerminalId TerminalId { get; private set; }

    public string CurrencyCode { get; private set; }

    public Currency Currency => Monetary.Currency.FromCode(CurrencyCode);

    public UserId OpenedBy { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>The counted float the drawer started with. CSH-01.</summary>
    public Money OpeningFloat { get; private set; }

    public ShiftStatus Status { get; private set; }

    public UserId? ClosedBy { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>What was physically counted at close. CSH-04.</summary>
    public Money? CountedCash { get; private set; }

    /// <summary>What the system says should be there. Frozen at close. CSH-04.</summary>
    public Money? ExpectedCash { get; private set; }

    /// <summary>Counted less expected. Negative means the drawer is short. CSH-04.</summary>
    public Money? Variance { get; private set; }

    public string? ClosingNotes { get; private set; }

    public IReadOnlyList<CashMovement> CashMovements => _cashMovements;

    public bool IsOpen => Status == ShiftStatus.Open;

    /// <summary>CSH-01.</summary>
    public static Shift Open(
        ShopId shopId,
        TerminalId terminalId,
        UserId openedBy,
        Money openingFloat,
        DateTimeOffset openedAt,
        ShiftId? id = null)
    {
        if (openingFloat.IsNegative)
        {
            throw new DomainException("The opening float cannot be negative.");
        }

        return new Shift(id ?? ShiftId.New(), shopId, terminalId, openedBy, openingFloat, openedAt);
    }

    /// <summary>CSH-05 and CSH-06.</summary>
    public CashMovement RecordCashMovement(
        CashMovementKind kind,
        Money amount,
        string reason,
        UserId recordedBy,
        DateTimeOffset occurredAt)
    {
        RequireOpen();
        RequireCurrency(amount);

        var movement = CashMovement.Create(Id, ShopId, kind, amount, reason, recordedBy, occurredAt);
        _cashMovements.Add(movement);
        return movement;
    }

    /// <summary>Net effect of pay-ins, pay-outs and drops on the drawer.</summary>
    public Money NetCashMovements =>
        Money.Sum(_cashMovements.Select(m => m.SignedAmount), Currency);

    /// <summary>
    /// What the drawer should hold right now. CSH-04.
    /// </summary>
    /// <remarks>
    /// Only cash counts. Card and QR takings never reach the drawer, and including them is
    /// the most common way a Z report ends up unexplainable.
    /// </remarks>
    public Money ExpectedCashFrom(ShiftSalesSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        RequireCurrency(summary.CashSales);

        return OpeningFloat + summary.CashSales - summary.CashRefunds + NetCashMovements;
    }

    /// <summary>
    /// The mid-shift X report. CSH-03.
    /// </summary>
    /// <remarks>
    /// Non-closing by design: a manager checking the drawer at lunchtime must not end the
    /// cashier's shift as a side effect.
    /// </remarks>
    public ShiftReport ReportX(ShiftSalesSummary summary, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(summary);
        RequireOpen();

        return new ShiftReport(
            Id,
            ShopId,
            TerminalId,
            ShiftReportKind.X,
            OpenedAt,
            at,
            OpeningFloat,
            summary,
            NetCashMovements,
            ExpectedCashFrom(summary),
            CountedCash: null,
            Variance: null);
    }

    /// <summary>
    /// Closes the shift and produces the Z report. CSH-04.
    /// </summary>
    /// <param name="countedCash">
    /// What was physically counted. CSH-07 expects the cashier to enter this before seeing
    /// the expected figure, which is a UI obligation the domain supports by taking the count
    /// as an input rather than offering the expectation first.
    /// </param>
    public ShiftReport Close(
        ShiftSalesSummary summary,
        Money countedCash,
        UserId closedBy,
        DateTimeOffset closedAt,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        RequireOpen();
        RequireCurrency(countedCash);

        if (countedCash.IsNegative)
        {
            throw new DomainException("The counted cash cannot be negative.");
        }

        var expected = ExpectedCashFrom(summary);

        Status = ShiftStatus.Closed;
        ClosedBy = closedBy;
        ClosedAt = closedAt;
        CountedCash = countedCash;
        ExpectedCash = expected;
        Variance = countedCash - expected;
        ClosingNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        return new ShiftReport(
            Id,
            ShopId,
            TerminalId,
            ShiftReportKind.Z,
            OpenedAt,
            closedAt,
            OpeningFloat,
            summary,
            NetCashMovements,
            expected,
            countedCash,
            Variance);
    }

    private void RequireOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException("This shift has been closed.");
        }
    }

    private void RequireCurrency(Money amount)
    {
        if (amount.Currency.Code != CurrencyCode)
        {
            throw new DomainException(
                $"This shift is in {CurrencyCode} but {amount.Currency.Code} was given.");
        }
    }
}
