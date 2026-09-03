using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Shifts;

/// <summary>Cash entering or leaving the drawer other than through a sale.</summary>
public enum CashMovementKind
{
    /// <summary>Money put into the drawer, for example change brought from the safe. CSH-05.</summary>
    PayIn = 0,

    /// <summary>Money taken out, for example paying a supplier in cash. CSH-05.</summary>
    PayOut = 1,

    /// <summary>Cash moved to the safe to keep the drawer light. CSH-06.</summary>
    Drop = 2,
}

/// <summary>
/// One movement of cash in or out of the drawer during a shift. CSH-05, CSH-06.
/// </summary>
/// <remarks>
/// Every one of these carries a reason. Unexplained cash movements are precisely the gap
/// that makes a Z report variance impossible to investigate a day later.
/// </remarks>
public sealed class CashMovement : Entity<CashMovementId>, IBelongsToShop
{
    private CashMovement(
        CashMovementId id,
        ShiftId shiftId,
        ShopId shopId,
        CashMovementKind kind,
        Money amount,
        string reason,
        UserId recordedBy,
        DateTimeOffset occurredAt)
        : base(id)
    {
        ShiftId = shiftId;
        ShopId = shopId;
        Kind = kind;
        Amount = amount;
        Reason = reason;
        RecordedBy = recordedBy;
        OccurredAt = occurredAt;
    }

    private CashMovement() => Reason = null!;

    public ShiftId ShiftId { get; private set; }

    public ShopId ShopId { get; private set; }

    public CashMovementKind Kind { get; private set; }

    /// <summary>Always positive. The direction is in <see cref="Kind"/>.</summary>
    public Money Amount { get; private set; }

    public string Reason { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The effect on the drawer, positive for a pay-in.</summary>
    public Money SignedAmount => Kind == CashMovementKind.PayIn ? Amount : Amount.Negate();

    internal static CashMovement Create(
        ShiftId shiftId,
        ShopId shopId,
        CashMovementKind kind,
        Money amount,
        string reason,
        UserId recordedBy,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!amount.IsPositive)
        {
            throw new DomainException("A cash movement must be more than zero.");
        }

        return new CashMovement(
            CashMovementId.New(),
            shiftId,
            shopId,
            kind,
            amount,
            reason.Trim(),
            recordedBy,
            occurredAt);
    }
}
