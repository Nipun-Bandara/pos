using RetailCore.Domain.Common;

namespace RetailCore.Domain.Tax;

/// <summary>
/// A tax rule that applies to products for a stated period. CMP-02.
/// </summary>
/// <remarks>
/// PLAN.md risk register: tax rules change, and a constant in code means a release under
/// time pressure on the day a budget takes effect. Rates are rows with an effective date,
/// so the new rate is entered in advance and starts applying on its own.
/// </remarks>
public sealed class TaxRate : Entity<TaxRateId>, IBelongsToShop
{
    private TaxRate(
        TaxRateId id,
        ShopId shopId,
        string code,
        string name,
        TaxCategory category,
        decimal rate,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
        : base(id)
    {
        ShopId = shopId;
        Code = code;
        Name = name;
        Category = category;
        Rate = rate;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    private TaxRate()
    {
        Code = null!;
        Name = null!;
    }

    /// <summary>TEN-01: every row belongs to exactly one shop.</summary>
    public ShopId ShopId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public TaxCategory Category { get; private set; }

    /// <summary>The rate as a fraction, so 18% is 0.18.</summary>
    public decimal Rate { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Null while the rate is open ended.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public static TaxRate Create(
        ShopId shopId,
        string code,
        string name,
        TaxCategory category,
        decimal rate,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo = null,
        TaxRateId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (category != TaxCategory.Standard && rate != 0m)
        {
            throw new DomainException(
                $"A {category} tax rate must be zero, but {rate} was supplied.");
        }

        if (effectiveTo is { } end && end < effectiveFrom)
        {
            throw new DomainException(
                $"Tax rate '{code}' ends on {end} which is before it starts on {effectiveFrom}.");
        }

        // Validated by constructing the treatment, which enforces the plausible range.
        _ = BuildTreatment(code, category, rate);

        return new TaxRate(
            id ?? TaxRateId.New(),
            shopId,
            code,
            name,
            category,
            rate,
            effectiveFrom,
            effectiveTo);
    }

    public bool IsEffectiveOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo.Value);

    /// <summary>Closes an open-ended rate so a successor can take over.</summary>
    public void EndOn(DateOnly lastEffectiveDate)
    {
        if (lastEffectiveDate < EffectiveFrom)
        {
            throw new DomainException(
                $"Tax rate '{Code}' cannot end on {lastEffectiveDate}, before it started on {EffectiveFrom}.");
        }

        EffectiveTo = lastEffectiveDate;
    }

    /// <summary>Copies this rule onto a sale line so the line stops depending on it.</summary>
    public TaxTreatment ToTreatment() => BuildTreatment(Code, Category, Rate);

    private static TaxTreatment BuildTreatment(string code, TaxCategory category, decimal rate) =>
        category switch
        {
            TaxCategory.Standard => TaxTreatment.Standard(code, rate),
            TaxCategory.ZeroRated => TaxTreatment.ZeroRated(code),
            TaxCategory.Exempt => TaxTreatment.Exempt(code),
            _ => throw new DomainException($"Unknown tax category {category}."),
        };
}
