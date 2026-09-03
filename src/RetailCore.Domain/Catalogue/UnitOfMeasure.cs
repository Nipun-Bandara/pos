namespace RetailCore.Domain.Catalogue;

/// <summary>
/// How a product is sold. CAT-01.
/// </summary>
public enum UnitOfMeasure
{
    /// <summary>Discrete items. Quantity is a whole number.</summary>
    Each = 0,

    Kilogram = 1,

    Gram = 2,

    Litre = 3,

    Millilitre = 4,

    Metre = 5,

    /// <summary>A multipack sold as one item.</summary>
    Pack = 6,
}

public static class UnitOfMeasureExtensions
{
    /// <summary>
    /// True when the unit admits fractional quantities, so the till should accept a weight
    /// rather than forcing whole units. SAL-05.
    /// </summary>
    public static bool AllowsFractionalQuantity(this UnitOfMeasure unit) =>
        unit is not (UnitOfMeasure.Each or UnitOfMeasure.Pack);

    /// <summary>The abbreviation printed on a receipt.</summary>
    public static string Abbreviation(this UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Each => "ea",
        UnitOfMeasure.Kilogram => "kg",
        UnitOfMeasure.Gram => "g",
        UnitOfMeasure.Litre => "L",
        UnitOfMeasure.Millilitre => "ml",
        UnitOfMeasure.Metre => "m",
        UnitOfMeasure.Pack => "pk",
        _ => string.Empty,
    };
}
