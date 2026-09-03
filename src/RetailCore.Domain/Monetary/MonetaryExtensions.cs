namespace RetailCore.Domain.Monetary;

/// <summary>
/// The one sanctioned way to turn a unit price and a quantity into a line amount.
/// </summary>
public static class MonetaryExtensions
{
    /// <summary>
    /// Extends a unit price over a quantity, rounding once to a whole minor unit.
    /// </summary>
    /// <remarks>
    /// Rounding happens here and only here. A line's extended amount is the rounded figure,
    /// and every total downstream is a sum of already-rounded lines. Rounding at the end
    /// instead would give a total that does not equal the sum of the printed line amounts,
    /// which customers do notice and cannot be argued with at the counter.
    /// </remarks>
    public static Money Extend(
        this Money unitPrice,
        Quantity quantity,
        MidpointRounding rounding = Money.RetailRounding) =>
        unitPrice.MultiplyBy(quantity.Value, rounding);
}
