using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Inventory;

public static class StockConversion
{
    public static Quantity ToBase(Quantity input, long factorThousandths)
    {
        if (!input.IsPositive || factorThousandths <= 0) throw new DomainException("InvalidStockQuantity");
        var scaled = (decimal)input.Thousandths * factorThousandths;
        if (scaled % 1000 != 0) throw new DomainException("StockPrecision");
        return Quantity.FromThousandths(checked((long)(scaled / 1000)));
    }
    public static long After(long balance, IEnumerable<(Quantity Quantity, StockDirection Direction)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
            balance = checked(balance + (change.Direction == StockDirection.In ? change.Quantity.Thousandths : -change.Quantity.Thousandths));
        return balance;
    }
}
