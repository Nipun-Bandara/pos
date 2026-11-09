using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>Immutable identity for an entire posting, including a damaged return with no stock effect.</summary>
public sealed class StockPostingReceipt : IBelongsToShop
{
    private StockPostingReceipt() { }
    internal StockPostingReceipt(ShopId shop, string sourceType, Guid sourceId, string fingerprint)
    { ShopId = shop; SourceType = sourceType; SourceId = sourceId; Fingerprint = fingerprint; }
    public ShopId ShopId { get; private set; }
    public string SourceType { get; private set; } = "";
    public Guid SourceId { get; private set; }
    public string Fingerprint { get; private set; } = "";
}
