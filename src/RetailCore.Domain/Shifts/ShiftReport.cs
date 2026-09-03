using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Shifts;

public enum ShiftReportKind
{
    /// <summary>Mid-shift, non-closing. CSH-03.</summary>
    X = 0,

    /// <summary>Produced when the shift closes. CSH-04.</summary>
    Z = 1,
}

/// <summary>
/// The figures printed on an X or Z report.
/// </summary>
/// <remarks>
/// A value object rather than a stored entity: it is derived entirely from the shift and the
/// sales of the period, so storing it would create a second version of the truth that could
/// drift. Reprinting a Z report recomputes it from the same immutable records.
/// </remarks>
public sealed record ShiftReport(
    ShiftId ShiftId,
    ShopId ShopId,
    TerminalId TerminalId,
    ShiftReportKind Kind,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    Money OpeningFloat,
    ShiftSalesSummary Sales,
    Money NetCashMovements,
    Money ExpectedCash,
    Money? CountedCash,
    Money? Variance)
{
    /// <summary>Everything taken, however it was paid.</summary>
    public Money GrossTakings => Sales.CashSales + Sales.NonCashSales;

    /// <summary>Everything given back.</summary>
    public Money TotalRefunds => Sales.CashRefunds + Sales.NonCashRefunds;

    /// <summary>Takings less refunds.</summary>
    public Money NetTakings => GrossTakings - TotalRefunds;

    /// <summary>True when the count and the expectation agree exactly.</summary>
    public bool IsBalanced => Variance is { } variance && variance.IsZero;

    /// <summary>True when the drawer holds less than it should. CSH-08 alerts on this.</summary>
    public bool IsShort => Variance is { } variance && variance.IsNegative;
}
