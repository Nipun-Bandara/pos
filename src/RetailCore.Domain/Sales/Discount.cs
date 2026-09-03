using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Sales;

public enum DiscountKind
{
    /// <summary>A proportion of the amount being discounted. SAL-07.</summary>
    Percentage = 0,

    /// <summary>A fixed sum off. SAL-07.</summary>
    FixedAmount = 1,
}

/// <summary>
/// A reduction applied to a line or to a whole sale, with the reason and who allowed it.
/// </summary>
/// <remarks>
/// SAL-07 and SAL-09. The reason and the authoriser are part of the value rather than
/// optional extras, because a discount is the single easiest way to walk stock out of a
/// shop and the audit trail (SEC-06) is only useful if it says who and why.
/// </remarks>
public sealed record Discount
{
    private Discount(
        DiscountKind kind,
        decimal percentage,
        long fixedAmountMinor,
        string currencyCode,
        string reason,
        UserId? authorisedBy)
    {
        Kind = kind;
        Percentage = percentage;
        FixedAmountMinor = fixedAmountMinor;
        CurrencyCode = currencyCode;
        Reason = reason;
        AuthorisedBy = authorisedBy;
    }

    public DiscountKind Kind { get; }

    /// <summary>A fraction, so 10% is 0.10. Zero for a fixed amount discount.</summary>
    public decimal Percentage { get; }

    /// <summary>
    /// The fixed amount in minor units, and the currency it is in.
    /// </summary>
    /// <remarks>
    /// Held as two scalars rather than a <see cref="Money"/> because a discount is an
    /// optional owned value, and EF Core cannot nest a complex type inside one. The domain
    /// still works in <see cref="FixedAmount"/>; only the storage shape is flat.
    /// </remarks>
    public long FixedAmountMinor { get; }

    public string CurrencyCode { get; }

    /// <summary>Zero for a percentage discount.</summary>
    public Money FixedAmount => Money.FromMinor(FixedAmountMinor, Monetary.Currency.FromCode(CurrencyCode));

    public string Reason { get; }

    /// <summary>
    /// The manager or owner who approved it, when approval was needed. SAL-09.
    /// Null means the discount was within the cashier's own limit.
    /// </summary>
    public UserId? AuthorisedBy { get; }

    public static Discount OfPercentage(
        decimal percentage,
        Currency currency,
        string reason,
        UserId? authorisedBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (percentage <= 0m || percentage > 1m)
        {
            throw new DomainException(
                $"A discount of {percentage:P2} is not valid. Express it as a fraction between 0 and 1, so 10% is 0.10.");
        }

        return new Discount(DiscountKind.Percentage, percentage, 0, currency.Code, reason.Trim(), authorisedBy);
    }

    public static Discount OfAmount(Money amount, string reason, UserId? authorisedBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!amount.IsPositive)
        {
            throw new DomainException("A discount must be greater than zero.");
        }

        return new Discount(
            DiscountKind.FixedAmount,
            0m,
            amount.MinorUnits,
            amount.CurrencyCode,
            reason.Trim(),
            authorisedBy);
    }

    /// <summary>
    /// The reduction this discount produces against the given amount.
    /// </summary>
    /// <remarks>
    /// Capped at the base amount so that a fixed discount larger than the line cannot drive
    /// the line negative. A negative line would look like a refund to every report
    /// downstream, and would let someone hand out cash through the discount key.
    /// </remarks>
    public Money ComputeOn(Money baseAmount)
    {
        if (!baseAmount.IsPositive)
        {
            return Money.Zero(baseAmount.Currency);
        }

        var raw = Kind switch
        {
            DiscountKind.Percentage => baseAmount.MultiplyBy(Percentage),
            DiscountKind.FixedAmount => FixedAmount,
            _ => throw new DomainException($"Unknown discount kind {Kind}."),
        };

        return Money.Min(raw, baseAmount);
    }

    /// <summary>
    /// The proportion of the base amount this discount takes, used to decide whether it
    /// crosses the shop's approval threshold. SAL-09.
    /// </summary>
    public decimal EffectiveRateOn(Money baseAmount)
    {
        if (!baseAmount.IsPositive)
        {
            return 0m;
        }

        return (decimal)ComputeOn(baseAmount).MinorUnits / baseAmount.MinorUnits;
    }

    public override string ToString() => Kind == DiscountKind.Percentage
        ? $"{Percentage:P2} ({Reason})"
        : $"{FixedAmount} ({Reason})";
}
