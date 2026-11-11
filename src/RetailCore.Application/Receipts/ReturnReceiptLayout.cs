using System.Globalization;
using System.Resources;

namespace RetailCore.Application.Receipts;

public static class ReturnReceiptLayout
{
    private static readonly ResourceManager Labels=new("RetailCore.Application.Receipts.ReturnReceiptLabels",typeof(ReturnReceiptLayout).Assembly);
    public static string Label(string key)=>Labels.GetString(key,CultureInfo.InvariantCulture)??throw new InvalidOperationException(key);
    public static IReadOnlyList<ReceiptRow> Build(ReceiptDocument d,bool copy)
    {
        string A(long minor)=>ReceiptLayout.Amount(minor,d.Currency);
        var rows=new List<ReceiptRow>{new(d.ShopName,true,ReceiptStyle.Title),new(Label(d.Kind),true,ReceiptStyle.Center)};
        void Pair(string key,string value)=>rows.Add(new(key+" : "+value,false,ReceiptStyle.Pair,key,value));
        if(copy) rows.Add(new(ReceiptLayout.Label("Copy"),true,ReceiptStyle.Center));
        Pair(ReceiptLayout.Label("Bill"),d.Number);Pair(Label("Original"),d.OriginalNumber??"");
        if(d.ReplacementNumber is { } replacement)Pair(Label("Replacement"),replacement);
        Pair(ReceiptLayout.Label("Date"),d.LocalTime);Pair(ReceiptLayout.Label("Cashier"),d.Cashier);Pair(Label("Reason"),d.Reason??"");
        foreach(var line in d.Returned??[])
            rows.Add(new(line.Name+" · "+(line.Thousandths/1000m).ToString("0.000",CultureInfo.InvariantCulture)+" "+line.Unit
                +" · "+A(line.CreditMinor)+" · "+Label(line.Saleable?"Saleable":"Damaged")));
        Pair(Label("Credit"),A(d.ReturnCreditMinor));
        if(d.ReplacementNumber is not null)
        {
            Pair(Label("ReplacementTotal"),A(d.TotalMinor));
            foreach(var line in d.Lines) rows.Add(new(line.Name+" · "+(line.QuantityThousandths/1000m).ToString("0.000",CultureInfo.InvariantCulture)+" × "+A(line.UnitPriceMinor)+" = "+A(line.TotalMinor)));
        }
        Pair(Label("Collected"),A(d.PaidMinor));Pair(Label("CashRefund"),A(d.CashPayoutMinor));Pair(Label("CardRefund"),A(d.CardPayoutMinor));
        Pair(ReceiptLayout.Label("Rounding"),A(d.RoundingMinor));Pair(ReceiptLayout.Label("Change"),A(d.ChangeMinor));
        rows.Add(new(SampleReceiptLayout.Label("ValidationPending"),false,ReceiptStyle.Center));return rows;
    }
}
