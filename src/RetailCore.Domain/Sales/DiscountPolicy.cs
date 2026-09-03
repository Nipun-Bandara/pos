using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Sales;

/// <summary>
/// How large a discount a cashier may give before a manager has to approve it. SAL-09.
/// </summary>
/// <remarks>
/// CFG-01: this is per-shop settings data, not a constant. A hardware shop haggling on a
/// large order and a pharmacy that never discounts want very different numbers.
/// </remarks>
public sealed record DiscountPolicy
{
    private DiscountPolicy(decimal maxUnauthorisedRate) => MaxUnauthorisedRate = maxUnauthorisedRate;

    /// <summary>The largest proportion a cashier may take off unaided. 0.10 is ten percent.</summary>
    public decimal MaxUnauthorisedRate { get; }

    /// <summary>A sensible default until the shop sets its own.</summary>
    public static DiscountPolicy Default { get; } = new(0.10m);

    /// <summary>Every discount needs a manager. Used by shops that do not discount at all.</summary>
    public static DiscountPolicy AlwaysRequireApproval { get; } = new(0m);

    public static DiscountPolicy UpTo(decimal maxUnauthorisedRate)
    {
        if (maxUnauthorisedRate is < 0m or > 1m)
        {
            throw new DomainException(
                $"A discount threshold of {maxUnauthorisedRate:P2} is not valid. Use a fraction between 0 and 1.");
        }

        return new DiscountPolicy(maxUnauthorisedRate);
    }

    /// <summary>True when this discount is large enough to need a manager's authorisation.</summary>
    public bool RequiresAuthorisation(Discount discount, Money baseAmount)
    {
        ArgumentNullException.ThrowIfNull(discount);

        return discount.EffectiveRateOn(baseAmount) > MaxUnauthorisedRate;
    }

    /// <summary>
    /// Throws unless the discount is either within the cashier's limit or carries an
    /// authoriser.
    /// </summary>
    /// <exception cref="DomainException">Approval is required and none was recorded.</exception>
    public void EnsureSatisfied(Discount discount, Money baseAmount)
    {
        ArgumentNullException.ThrowIfNull(discount);

        if (RequiresAuthorisation(discount, baseAmount) && discount.AuthorisedBy is null)
        {
            throw new DomainException(
                $"A discount of {discount.EffectiveRateOn(baseAmount):P2} needs a manager's approval. " +
                $"Cashiers may give up to {MaxUnauthorisedRate:P2}.");
        }
    }
}
