using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Sqlite.Foundation;

public sealed class ReceiptRecord
{
    private ReceiptRecord() { DocumentJson = null!; }
    public ReceiptRecord(ShopId shopId, Guid saleId, string documentJson) { ShopId = shopId; SaleId = new SaleId(saleId); DocumentJson = documentJson; }
    public SaleId SaleId { get; private set; }
    public ShopId ShopId { get; private set; }
    public string DocumentJson { get; private set; }
}
public sealed class PrintAttemptRecord
{
    private PrintAttemptRecord() { DocumentJson = null!; }
    public PrintAttemptRecord(ShopId shopId, Guid id, Guid saleId, string documentJson) { ShopId = shopId; Id = id; SaleId = new SaleId(saleId); DocumentJson = documentJson; }
    public Guid Id { get; private set; }
    public SaleId SaleId { get; private set; }
    public ShopId ShopId { get; private set; }
    public string DocumentJson { get; private set; }
}
public sealed class PrintOutcomeRecord
{
    private PrintOutcomeRecord() { DocumentJson = null!; }
    public PrintOutcomeRecord(Guid attemptId, string documentJson) { AttemptId = attemptId; DocumentJson = documentJson; }
    public Guid AttemptId { get; private set; }
    public string DocumentJson { get; private set; }
}
