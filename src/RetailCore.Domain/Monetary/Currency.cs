using RetailCore.Domain.Common;

namespace RetailCore.Domain.Monetary;

/// <summary>
/// A currency, its minor unit precision, and the smallest coin actually in circulation.
/// </summary>
/// <remarks>
/// <para>
/// PAY-02 requires cash rounding to the smallest circulating coin, which is not the same
/// as the smallest unit the currency is denominated in. Sri Lanka still prices in cents
/// but the smallest coin in circulation is one rupee, so a cash total of 249.60 is
/// collected as 250.00. Card and QR settle at the exact figure. Keeping both numbers on
/// the currency is what lets the domain get that right without hardcoding a country.
/// </para>
/// <para>
/// Only the ISO code is state; precision and the rounding increment are looked up from it.
/// That is what lets persistence store a single three character column and rebuild a fully
/// working currency from it, with no chance of a row carrying a code that disagrees with
/// the precision stored beside it.
/// </para>
/// </remarks>
public readonly record struct Currency
{
    private Currency(string code) => Code = code;

    /// <summary>ISO 4217 alphabetic code. The only stored state.</summary>
    public string Code { get; }

    /// <summary>
    /// False for <c>default(Currency)</c>. Money guards on this so that a zero-initialised
    /// struct can never masquerade as a real amount.
    /// </summary>
    public bool IsSpecified => Code is not null;

    /// <summary>Digits after the decimal point, for example 2 for rupees and cents.</summary>
    public int MinorUnitDigits => Definition.MinorUnitDigits;

    /// <summary>
    /// The smallest coin in circulation, expressed in minor units. 1 means no rounding.
    /// </summary>
    public long CashRoundingIncrementMinor => Definition.CashRoundingIncrementMinor;

    /// <summary>For example 100 when there are two minor unit digits.</summary>
    public long MinorUnitsPerMajor => Definition.MinorUnitsPerMajor;

    /// <summary>Sri Lankan rupee. Cents are quoted but the smallest coin is one rupee.</summary>
    public static Currency LKR { get; } = new("LKR");

    public static Currency USD { get; } = new("USD");

    public static Currency EUR { get; } = new("EUR");

    public static Currency GBP { get; } = new("GBP");

    /// <summary>Indian rupee. Paise coins are effectively out of circulation.</summary>
    public static Currency INR { get; } = new("INR");

    /// <summary>Zero-decimal currency, useful for proving the model is not cent-specific.</summary>
    public static Currency JPY { get; } = new("JPY");

    private sealed record CurrencyDefinition(int MinorUnitDigits, long CashRoundingIncrementMinor)
    {
        public long MinorUnitsPerMajor { get; } = MinorUnitDigits switch
        {
            0 => 1L,
            1 => 10L,
            2 => 100L,
            3 => 1_000L,
            _ => throw new DomainException("Unsupported currency precision."),
        };
    }

    private static readonly Dictionary<string, CurrencyDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LKR"] = new(2, 100),
            ["USD"] = new(2, 1),
            ["EUR"] = new(2, 1),
            ["GBP"] = new(2, 1),
            ["INR"] = new(2, 100),
            ["JPY"] = new(0, 1),
        };

    private CurrencyDefinition Definition =>
        Code is not null && Definitions.TryGetValue(Code, out var definition)
            ? definition
            : throw new DomainException(
                Code is null
                    ? "An amount was used without a currency."
                    : $"Currency '{Code}' is not configured in this build.");

    /// <summary>Looks up a currency by ISO code, for rehydrating persisted money.</summary>
    /// <exception cref="DomainException">The code is not one this build knows about.</exception>
    public static Currency FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return Definitions.ContainsKey(code)
            ? new Currency(code.ToUpperInvariant())
            : throw new DomainException($"Currency '{code}' is not configured in this build.");
    }

    public static bool TryFromCode(string? code, out Currency currency)
    {
        if (code is not null && Definitions.ContainsKey(code))
        {
            currency = new Currency(code.ToUpperInvariant());
            return true;
        }

        currency = default;
        return false;
    }

    /// <summary>Every currency this build knows about, for validation and seeding.</summary>
    public static IReadOnlyCollection<string> KnownCodes => Definitions.Keys;

    public override string ToString() => Code ?? "(unspecified)";
}
