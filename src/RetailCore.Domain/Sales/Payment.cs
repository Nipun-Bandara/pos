using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Sales;

/// <summary>
/// How a sale was settled. PAY-01 to PAY-05.
/// </summary>
public enum PaymentMethod
{
    /// <summary>PAY-01. The only method that can be over-tendered and give change.</summary>
    Cash = 0,

    /// <summary>PAY-03. Settled on the terminal's own card machine; only the reference is kept.</summary>
    Card = 1,

    /// <summary>PAY-04.</summary>
    Qr = 2,

    /// <summary>PAY-07. Deferred to Phase 6, modelled now so the totals do not need reworking.</summary>
    ShopCredit = 3,

    /// <summary>A voucher or gift note accepted at face value.</summary>
    Voucher = 4,
    ExchangeCredit = 5,
}

/// <summary>
/// One tender against a sale. A sale may have several. PAY-05.
/// </summary>
/// <remarks>
/// CMP-06 and PAY-08: no card data is held. The <see cref="Reference"/> is the approval
/// code the card machine prints, nothing more. The system stays out of PCI scope by never
/// touching a card number, and that is a property to preserve, not an implementation detail.
/// </remarks>
public sealed class Payment : Entity<PaymentId>, IBelongsToShop
{
    private Payment(
        PaymentId id,
        SaleId saleId,
        ShopId shopId,
        PaymentMethod method,
        Money amount,
        Money tendered,
        string? reference,
        DateTimeOffset takenAt)
        : base(id)
    {
        SaleId = saleId;
        ShopId = shopId;
        Method = method;
        Amount = amount;
        Tendered = tendered;
        Reference = reference;
        TakenAt = takenAt;
    }

    private Payment()
    {
    }

    public SaleId SaleId { get; private set; }

    /// <summary>TEN-01. Carried on the tender so row level security can cover this table too.</summary>
    public ShopId ShopId { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>What this tender settles against the sale.</summary>
    public Money Amount { get; private set; }

    /// <summary>
    /// What the customer handed over. Equal to <see cref="Amount"/> for everything except
    /// over-tendered cash, where the difference is the change.
    /// </summary>
    public Money Tendered { get; private set; }

    /// <summary>Approval or transaction reference from the card machine or wallet. Never card data.</summary>
    public string? Reference { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    /// <summary>Change handed back for this tender. Non-zero only for over-tendered cash.</summary>
    public Money Change => Tendered - Amount;

    internal static Payment Create(
        SaleId saleId,
        ShopId shopId,
        PaymentMethod method,
        Money amount,
        Money tendered,
        string? reference,
        DateTimeOffset takenAt,
        PaymentId? id = null, bool allowZeroCash = false)
    {
        if (!amount.IsPositive && !(allowZeroCash && method == PaymentMethod.Cash && amount.IsZero))
        {
            throw new DomainException("A payment must be greater than zero.");
        }

        if (tendered < amount)
        {
            throw new DomainException(
                $"Tendered {tendered} is less than the {amount} being applied to the sale.");
        }

        if (method != PaymentMethod.Cash && tendered != amount)
        {
            throw new DomainException(
                $"{method} payments cannot give change, so tendered must equal the amount applied.");
        }

        if (method == PaymentMethod.Card && !ExternalCardReference.IsValid(reference))
            throw new DomainException("InvalidCardReference");
        return new Payment(
            id ?? PaymentId.New(),
            saleId,
            shopId,
            method,
            amount,
            tendered,
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            takenAt);
    }
}
