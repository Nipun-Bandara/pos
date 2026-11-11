using RetailCore.Application.Receipts;
using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;

namespace RetailCore.Application.Returns;

public sealed record ReturnSelection(SaleLineId LineId, long Thousandths, ReturnDisposition Disposition);
public sealed record ReturnDraft(Guid OperationId, Guid OriginalSaleId, IReadOnlyList<ReturnSelection> Items, string Reason,
    bool UseBasket = false, bool Void = false, PaymentMethod CollectionMethod = PaymentMethod.Cash, long TenderedMinor = 0,
    bool CollectionCardApproved = false, bool RefundCardApproved = false, string? CardReference = null,
    bool AllowNegative = false, string? NegativeReason = null);
public sealed record ReturnLineView(SaleLineId Id, string Name, decimal Sold, decimal Remaining, decimal CreditRemaining, string Unit);
public sealed record ReturnBill(Guid Id, string Number, string Currency, IReadOnlyList<ReturnLineView> Lines, IReadOnlyList<ReturnResult> History);
public sealed record ReturnStock(string Name,string Unit,decimal Before,decimal Returned,decimal Consumed,decimal After);
public sealed record ReturnPreview(string Hash, long CreditMinor, long ReplacementMinor, long AppliedCreditMinor,
    long DueMinor, long RoundingMinor, long CashPayoutMinor, long CardPayoutMinor, long CashCreditMinor, long CardCreditMinor,
    long CreditCashUsed, long CreditCardUsed, string Currency, bool NegativeStock, IReadOnlyList<RetailCore.Application.Checkout.BillingLine> ReplacementLines,IReadOnlyList<ReturnStock> Stock);
public sealed record ReturnResult(Guid Id, Guid OriginalSaleId, Guid RefundId, Guid? ReplacementSaleId, string Number,
    string? ReplacementNumber, long CreditMinor, long AppliedCreditMinor, long CollectedMinor, long CashPayoutMinor,
    long CardPayoutMinor, long RoundingMinor, long CashTenderedMinor, long ChangeMinor, ReceiptDocument Document, bool IsVoid);
public sealed record ReceiptReturnLine(string Name, long Thousandths, string Unit, long CreditMinor, bool Saleable);
