using RetailCore.Domain.Common;
namespace RetailCore.Infrastructure.Sqlite.Foundation;
public sealed class ReturnRecord
{
    private ReturnRecord(){ResultJson=null!;}
    public ReturnRecord(ShopId shop,Guid id,Guid original,string json){ShopId=shop;Id=id;OriginalSaleId=new(original);ResultJson=json;}
    public Guid Id{get;private set;}
    public ShopId ShopId{get;private set;}
    public SaleId OriginalSaleId{get;private set;}
    public string ResultJson{get;private set;}
}
public sealed class ReturnPrintRecord
{
    private ReturnPrintRecord(){DocumentJson=null!;}
    public ReturnPrintRecord(ShopId shop,Guid id,Guid document,string json){ShopId=shop;Id=id;DocumentId=document;DocumentJson=json;}
    public Guid Id{get;private set;}
    public ShopId ShopId{get;private set;}
    public Guid DocumentId{get;private set;}
    public string DocumentJson{get;private set;}
}
public sealed class ReturnPrintOutcomeRecord
{
    private ReturnPrintOutcomeRecord(){DocumentJson=null!;}
    public ReturnPrintOutcomeRecord(Guid id,string json){Id=id;DocumentJson=json;}
    public Guid Id{get;private set;}
    public string DocumentJson{get;private set;}
}
