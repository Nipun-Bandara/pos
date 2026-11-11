using System.Numerics;
using RetailCore.Domain.Common;

namespace RetailCore.Domain.Sales;

/// <summary>Allocate the original collected amount, including its rounding, once across original lines.</summary>
public static class ReturnAmounts
{
    public static long Share(long amount, long numerator, long denominator)
    {
        if (amount < 0 || numerator < 0 || denominator <= 0 || numerator > denominator) throw new DomainException("InvalidReturnHistory");
        var product = (BigInteger)amount * numerator;
        return checked((long)((product * 2 + denominator) / (2 * (BigInteger)denominator)));
    }
    public static IReadOnlyDictionary<SaleLineId,long> Entitlements(Sale sale)
    {
        var result = new Dictionary<SaleLineId,long>(); long cumulative = 0, allocated = 0;
        foreach (var line in sale.ActiveLines.OrderBy(l => l.LineNumber))
        {
            cumulative = checked(cumulative + line.LineTotal.MinorUnits);
            var target = sale.Total.IsZero ? 0 : Share(sale.AmountSettled.MinorUnits,cumulative,sale.Total.MinorUnits);
            result.Add(line.Id, checked(target-allocated)); allocated=target;
        }
        return result;
    }
}
