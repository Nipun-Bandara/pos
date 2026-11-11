using RetailCore.Domain.Sales;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Security;
namespace RetailCore.Application.Reporting;

public sealed record TradingRecords(IReadOnlyList<Sale> Sales, IReadOnlyList<Refund> Refunds,
    IReadOnlyList<Shift> Shifts, IReadOnlyList<AuditEntry> Audit);
public sealed record ReportPeriod(DateOnly From, DateOnly Through);
/// <summary>Cells are text, decimal or local DateTime. Text is never a spreadsheet formula.</summary>
public sealed record ReportTable(string Title, string Context, IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);
public sealed record CashRequest(Guid Id, Guid ShiftId, bool Close, long Minor, bool PayOut, string Reason, long ExpectedMinor);
public sealed record CashResult(Guid ShiftId, long ExpectedMinor, long? CountedMinor, long? VarianceMinor);
public sealed record CashReview(Guid ShiftId, long ExpectedMinor, string Currency);
