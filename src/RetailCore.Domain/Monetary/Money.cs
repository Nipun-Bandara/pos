using System.Globalization;
using RetailCore.Domain.Common;

namespace RetailCore.Domain.Monetary;

/// <summary>
/// An exact amount of a single currency, held in minor units.
/// </summary>
/// <remarks>
/// <para>
/// Every monetary figure in this system is a <see cref="Money"/>. There is an architecture
/// test that fails the build if a raw <see cref="decimal"/> named like money appears on a
/// domain type, because decimal invites two specific bugs that are expensive in retail:
/// silently adding amounts in different currencies, and accumulating fractional cents that
/// never reconcile against the physical drawer.
/// </para>
/// <para>
/// Storing minor units as <see cref="long"/> makes addition and subtraction exact. The only
/// place rounding can enter is multiplication, and every multiplication here forces the
/// caller to accept a rounding mode.
/// </para>
/// <para>
/// The state is two scalars, the minor units and the ISO code, so that persistence maps it
/// to two ordinary columns. That keeps <c>SUM(TotalMinor)</c> available to the reporting
/// queries, which a single opaque column would not.
/// </para>
/// </remarks>
public readonly record struct Money : IComparable<Money>, IComparable, IFormattable
{
    /// <summary>
    /// The rounding used for retail money unless a caller asks for something else.
    /// Half away from zero is what a shopper expects and what tax authorities publish;
    /// banker's rounding would quietly bias totals on repeated half-cent results.
    /// </summary>
    public const MidpointRounding RetailRounding = MidpointRounding.AwayFromZero;

    private Money(long minorUnits, string currencyCode)
    {
        MinorUnits = minorUnits;
        CurrencyCode = currencyCode;
    }

    /// <summary>The amount in the currency's smallest denominated unit, for example cents.</summary>
    public long MinorUnits { get; }

    /// <summary>ISO 4217 code. Null only for <c>default(Money)</c>, which is never valid.</summary>
    public string CurrencyCode { get; }

    public Currency Currency => Monetary.Currency.FromCode(RequireCode());

    public bool IsZero => MinorUnits == 0;

    public bool IsPositive => MinorUnits > 0;

    public bool IsNegative => MinorUnits < 0;

    public static Money Zero(Currency currency) => new(0, RequireSpecified(currency));

    /// <summary>Builds an amount from minor units, for example 24960 cents.</summary>
    public static Money FromMinor(long minorUnits, Currency currency) =>
        new(minorUnits, RequireSpecified(currency));

    /// <summary>
    /// Builds an amount from a major unit figure such as 249.60. Use this at the edges of
    /// the system, where prices arrive from a user or a file, not in calculations.
    /// </summary>
    /// <exception cref="DomainException">The value carries more precision than the currency has.</exception>
    public static Money FromMajor(decimal amount, Currency currency)
    {
        var code = RequireSpecified(currency);

        var scaled = amount * currency.MinorUnitsPerMajor;
        var rounded = Math.Round(scaled, 0, RetailRounding);

        if (scaled != rounded)
        {
            throw new DomainException(
                $"{amount} has more precision than {code} supports. " +
                $"Round it deliberately before converting to money.");
        }

        return new Money((long)rounded, code);
    }

    /// <summary>
    /// Same as <see cref="FromMajor"/> but rounds instead of rejecting excess precision.
    /// Reserved for computed inputs, never for user entry.
    /// </summary>
    public static Money FromMajorRounded(
        decimal amount,
        Currency currency,
        MidpointRounding rounding = RetailRounding)
    {
        var code = RequireSpecified(currency);
        var scaled = Math.Round(amount * currency.MinorUnitsPerMajor, 0, rounding);
        return new Money((long)scaled, code);
    }

    /// <summary>The amount in major units. For display and export only, never for arithmetic.</summary>
    public decimal ToDecimal() => (decimal)MinorUnits / Currency.MinorUnitsPerMajor;

    public Money Abs() => new(Math.Abs(MinorUnits), RequireCode());

    public Money Negate() => new(-MinorUnits, RequireCode());

    /// <summary>
    /// Rounds to the smallest coin in circulation. PAY-02.
    /// </summary>
    /// <remarks>
    /// Applied to the cash portion of a settlement only. Card and QR settle exactly, so
    /// rounding them would create a permanent discrepancy against the acquirer.
    /// </remarks>
    public Money RoundToCashIncrement(MidpointRounding rounding = RetailRounding)
    {
        var increment = Currency.CashRoundingIncrementMinor;
        if (increment <= 1)
        {
            return this;
        }

        var quotient = (decimal)MinorUnits / increment;
        var rounded = (long)Math.Round(quotient, 0, rounding);
        return new Money(rounded * increment, CurrencyCode);
    }

    /// <summary>Multiplies by a whole number. Exact, no rounding possible.</summary>
    public Money MultiplyBy(long factor) => new(MinorUnits * factor, RequireCode());

    /// <summary>
    /// Multiplies by a fractional factor such as a quantity or a tax rate, rounding the
    /// result to a whole minor unit.
    /// </summary>
    public Money MultiplyBy(decimal factor, MidpointRounding rounding = RetailRounding) =>
        new((long)Math.Round(MinorUnits * factor, 0, rounding), RequireCode());

    /// <summary>
    /// Splits this amount across the given weights so that the parts sum back to exactly
    /// this amount, with no minor unit created or lost.
    /// </summary>
    /// <remarks>
    /// Needed wherever a figure computed on the whole sale has to be attributed back to
    /// individual lines: a whole-sale discount (SAL-08) and the tax analysis that follows
    /// it. Naively rounding each share independently loses or invents cents, and those
    /// cents show up as an unexplained variance on the Z report.
    /// <para>
    /// Uses the largest remainder method: floor every share, then hand the leftover units
    /// one at a time to the shares with the largest discarded fraction.
    /// </para>
    /// </remarks>
    /// <exception cref="DomainException">No weights were supplied, or they sum to zero.</exception>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var code = RequireCode();

        if (weights.Count == 0)
        {
            throw new DomainException("Cannot allocate an amount across no shares.");
        }

        if (weights.Any(w => w < 0))
        {
            throw new DomainException("Allocation weights cannot be negative.");
        }

        Int128 totalWeight = 0;
        foreach (var weight in weights)
        {
            totalWeight += weight;
        }

        if (totalWeight == 0)
        {
            throw new DomainException("Cannot allocate an amount across shares that are all zero.");
        }

        // Work on the magnitude so that truncation behaves the same for refunds, then
        // reapply the sign at the end.
        var sign = MinorUnits < 0 ? -1 : 1;
        Int128 magnitude = Math.Abs(MinorUnits);

        var shares = new long[weights.Count];
        var remainders = new Int128[weights.Count];
        Int128 distributed = 0;

        for (var i = 0; i < weights.Count; i++)
        {
            var numerator = magnitude * weights[i];
            var share = numerator / totalWeight;
            shares[i] = (long)share;
            remainders[i] = numerator - (share * totalWeight);
            distributed += share;
        }

        var leftover = magnitude - distributed;

        // Hand out the leftover units, largest discarded fraction first. Ties go to the
        // earlier line, which keeps allocation deterministic and therefore testable.
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .ToArray();

        for (var i = 0; leftover > 0; i = (i + 1) % order.Length)
        {
            shares[order[i]]++;
            leftover--;
        }

        return [.. shares.Select(s => new Money(s * sign, code))];
    }

    public static Money operator +(Money left, Money right) =>
        new(left.MinorUnits + right.MinorUnits, RequireSameCurrency(left, right));

    public static Money operator -(Money left, Money right) =>
        new(left.MinorUnits - right.MinorUnits, RequireSameCurrency(left, right));

    public static Money operator -(Money value) => value.Negate();

    public static bool operator <(Money left, Money right)
    {
        RequireSameCurrency(left, right);
        return left.MinorUnits < right.MinorUnits;
    }

    public static bool operator >(Money left, Money right)
    {
        RequireSameCurrency(left, right);
        return left.MinorUnits > right.MinorUnits;
    }

    public static bool operator <=(Money left, Money right) => !(left > right);

    public static bool operator >=(Money left, Money right) => !(left < right);

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Max(Money left, Money right) => left >= right ? left : right;

    public static Money Min(Money left, Money right) => left <= right ? left : right;

    /// <summary>Sums a sequence, using the supplied currency when the sequence is empty.</summary>
    public static Money Sum(IEnumerable<Money> amounts, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var total = Zero(currency);
        foreach (var amount in amounts)
        {
            total += amount;
        }

        return total;
    }

    public int CompareTo(Money other)
    {
        RequireSameCurrency(this, other);
        return MinorUnits.CompareTo(other.MinorUnits);
    }

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Money other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare money with {obj.GetType().Name}.", nameof(obj)),
    };

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (CurrencyCode is null)
        {
            return "(unspecified)";
        }

        var value = ToDecimal().ToString(
            format ?? "F" + Currency.MinorUnitDigits.ToString(CultureInfo.InvariantCulture),
            formatProvider ?? CultureInfo.InvariantCulture);

        return $"{CurrencyCode} {value}";
    }

    private static string RequireSpecified(Currency currency) =>
        currency.IsSpecified
            ? currency.Code
            : throw new DomainException(
                "An amount was created without a currency. Use Money.Zero(currency) rather than default.");

    private string RequireCode() =>
        CurrencyCode
        ?? throw new DomainException(
            "An amount was used without a currency. Use Money.Zero(currency) rather than default.");

    private static string RequireSameCurrency(Money left, Money right)
    {
        var leftCode = left.RequireCode();
        var rightCode = right.RequireCode();

        return leftCode == rightCode
            ? leftCode
            : throw new DomainException($"Cannot combine {leftCode} with {rightCode}.");
    }
}
