using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Sqlite.Foundation;

/// <summary>Recoverable per-user cart and last committed result; no card credentials.</summary>
public sealed class BillingBasketRecord : IBelongsToShop
{
    private BillingBasketRecord() { }
    internal BillingBasketRecord(ShopId shop, TerminalId terminal, UserId user, string document)
    { ShopId = shop; TerminalId = terminal; UserId = user; DocumentJson = document; }
    public ShopId ShopId { get; private set; }
    public TerminalId TerminalId { get; private set; }
    public UserId UserId { get; private set; }
    public string DocumentJson { get; private set; } = "";
    internal void Update(string document) => DocumentJson = document;
}
