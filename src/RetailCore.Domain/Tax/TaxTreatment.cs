using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Tax;

/// <summary>
/// The tax rule as it stood when a sale line was rung up, copied onto that line.
/// </summary>
/// <remarks>
/// CAT-05 and CMP-02. The rate is snapshotted, not referenced, because a receipt reprinted
/// after a budget changes the VAT rate must still show the tax that was actually charged.
/// A foreign key to the live rate record would silently rewrite history.
/// </remarks>
public sealed record TaxTreatment
{
    private TaxTreatment(string code, TaxCategory category, decimal rate)
    {
        Code = code;
        Category = category;
        Rate = rate;
    }

    /// <summary>Short code as printed on the receipt, for example "VAT18".</summary>
    public string Code { get; }

    public TaxCategory Category { get; }

    /// <summary>The rate as a fraction, so 18% is 0.18. Always zero for exempt and zero-rated.</summary>
    public decimal Rate { get; }

    public bool IsTaxable => Category == TaxCategory.Standard && Rate > 0m;

    public static TaxTreatment Standard(string code, decimal rate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (rate < 0m || rate >= 1m)
        {
            throw new DomainException(
                $"A tax rate of {rate:P2} is not plausible. Express the rate as a fraction, so 18% is 0.18.");
        }

        return new TaxTreatment(code, TaxCategory.Standard, rate);
    }

    public static TaxTreatment ZeroRated(string code = "ZERO")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new TaxTreatment(code, TaxCategory.ZeroRated, 0m);
    }

    public static TaxTreatment Exempt(string code = "EXEMPT")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new TaxTreatment(code, TaxCategory.Exempt, 0m);
    }

    /// <summary>
    /// Pulls the tax out of a tax-inclusive amount. CMP-01.
    /// </summary>
    /// <remarks>
    /// Shelf prices in these markets include tax, so the price the customer sees is the
    /// gross. The tax is therefore <c>gross × rate ÷ (1 + rate)</c>, not <c>gross × rate</c>.
    /// Using the second formula on a tax-inclusive price overstates the tax by the rate
    /// squared, which at 18% is a 3% error on every line, in the shop's favour, and is the
    /// kind of thing that gets found during an assessment.
    /// </remarks>
    /// <param name="grossAmount">The tax-inclusive amount for the line.</param>
    /// <returns>The tax contained within <paramref name="grossAmount"/>.</returns>
    public Money TaxWithin(Money grossAmount)
    {
        if (!IsTaxable)
        {
            return Money.Zero(grossAmount.Currency);
        }

        return grossAmount.MultiplyBy(Rate / (1m + Rate));
    }

    /// <summary>The tax-exclusive value of a tax-inclusive amount.</summary>
    public Money NetWithin(Money grossAmount) => grossAmount - TaxWithin(grossAmount);

    public override string ToString() => Category switch
    {
        TaxCategory.Standard => $"{Code} {Rate:P2}",
        _ => $"{Code} ({Category})",
    };
}
