using System.Globalization;
using RetailCore.Domain.Common;

namespace RetailCore.Domain.Monetary;

/// <summary>
/// How much of a product a line covers: three whole tins, or 0.285 kg of tomatoes.
/// </summary>
/// <remarks>
/// <para>
/// SAL-05 fixes weighed items at three decimal places, which is one gram when the unit is
/// a kilogram. That is the precision a shop scale reports, so carrying more would be false
/// accuracy and carrying less would lose money on cheap goods sold by weight.
/// </para>
/// <para>
/// Held as an integer count of thousandths, for the same reason money is held in minor
/// units: addition is exact, and the value aggregates in SQL on both providers. SQLite has
/// no real decimal type and cannot sum one, so a decimal column here would make
/// <c>SUM</c> over the stock ledger (INV-02) impossible on the till.
/// </para>
/// </remarks>
public readonly record struct Quantity : IComparable<Quantity>, IComparable, IFormattable
{
    /// <summary>Decimal places retained. One gram when the unit is a kilogram.</summary>
    public const int Scale = 3;

    private const decimal ScaleFactor = 1000m;

    private Quantity(long thousandths) => Thousandths = thousandths;

    /// <summary>The quantity as a whole number of thousandths. The stored form.</summary>
    public long Thousandths { get; }

    /// <summary>The quantity as a decimal, for display and arithmetic against prices.</summary>
    public decimal Value => Thousandths / ScaleFactor;

    public bool IsZero => Thousandths == 0;

    public bool IsPositive => Thousandths > 0;

    public static Quantity Zero { get; }

    public static Quantity One { get; } = new(1000);

    /// <summary>
    /// Creates a quantity, rejecting anything negative or finer than the supported scale.
    /// </summary>
    /// <exception cref="DomainException">
    /// The value is negative, or carries more than three decimal places. Excess precision is
    /// rejected rather than rounded: silently turning 0.2857 kg into 0.286 kg would make a
    /// receipt disagree with the scale display in front of the customer.
    /// </exception>
    public static Quantity Of(decimal value)
    {
        if (value < 0m)
        {
            throw new DomainException(
                "Quantity cannot be negative. Record a refund or a stock adjustment instead.");
        }

        var scaled = value * ScaleFactor;
        if (scaled != Math.Truncate(scaled))
        {
            throw new DomainException(
                $"Quantity {value} is finer than {Scale} decimal places, which this system does not carry.");
        }

        return new Quantity((long)scaled);
    }

    /// <summary>Creates a quantity from a whole number of units.</summary>
    public static Quantity Of(int units) => Of((decimal)units);

    /// <summary>Creates a quantity directly from thousandths, for rehydrating stored values.</summary>
    public static Quantity FromThousandths(long thousandths)
    {
        if (thousandths < 0)
        {
            throw new DomainException("Quantity cannot be negative.");
        }

        return new Quantity(thousandths);
    }

    /// <summary>
    /// Creates a quantity from a computed value, rounding to the supported scale. For
    /// scale readings and arithmetic results, never for figures a cashier typed.
    /// </summary>
    public static Quantity Rounded(decimal value, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        if (value < 0m)
        {
            throw new DomainException("Quantity cannot be negative.");
        }

        return new Quantity((long)Math.Round(value * ScaleFactor, 0, rounding));
    }

    /// <summary>True when this is a whole number of units, so no decimal need be shown.</summary>
    public bool IsWholeUnits => Thousandths % 1000 == 0;

    public static Quantity operator +(Quantity left, Quantity right) =>
        new(left.Thousandths + right.Thousandths);

    public static Quantity operator -(Quantity left, Quantity right)
    {
        var result = left.Thousandths - right.Thousandths;
        if (result < 0)
        {
            throw new DomainException(
                $"Subtracting {right} from {left} would give a negative quantity.");
        }

        return new Quantity(result);
    }

    public static bool operator <(Quantity left, Quantity right) => left.Thousandths < right.Thousandths;

    public static bool operator >(Quantity left, Quantity right) => left.Thousandths > right.Thousandths;

    public static bool operator <=(Quantity left, Quantity right) => left.Thousandths <= right.Thousandths;

    public static bool operator >=(Quantity left, Quantity right) => left.Thousandths >= right.Thousandths;

    public static Quantity Add(Quantity left, Quantity right) => left + right;

    public static Quantity Subtract(Quantity left, Quantity right) => left - right;

    public int CompareTo(Quantity other) => Thousandths.CompareTo(other.Thousandths);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Quantity other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare quantity with {obj.GetType().Name}.", nameof(obj)),
    };

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        IsWholeUnits && format is null
            ? (Thousandths / 1000).ToString(CultureInfo.InvariantCulture)
            : Value.ToString(format ?? "0.###", formatProvider ?? CultureInfo.InvariantCulture);
}
