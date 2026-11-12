using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using RetailCore.Application.Foundation;
using RetailCore.Application.Receipts;
using RetailCore.Application.Returns;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private sealed class ReturnEditorLine
    {
        public required ReturnLineView Original{get;init;}
        public decimal Requested{get;set;}
        public ReturnDisposition Disposition{get;set;}
    }
    private Guid? _returnBillId;
    private Guid _returnOperation=Guid.NewGuid();
    private List<ReturnEditorLine> _returnLines=[];
    private ReturnDraft? _returnDraft;
    private ReturnPreview? _returnPreview;
    private void ResetReturnEditor(){_returnBillId=null;_returnLines=[];_returnDraft=null;_returnPreview=null;_returnOperation=Guid.NewGuid();}
    private ScrollViewer ReturnsView()
    {
        var panel=new StackPanel();Label(panel,"Returns");
        panel.Children.Add(new TextBlock{Text=Texts.Get("ReturnHelp"),TextWrapping=TextWrapping.Wrap});
        var lookup=Field(panel,"ReturnBillLookup",_vm.ReturnBill?.Number);
        async Task Find(){ResetReturnEditor();await _vm.FindReturnBillAsync(lookup.Text);}
        panel.Children.Add(Button("FindReturnBill",Find));
        lookup.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await _vm.RunAsync(Find);}};
        if(_vm.ReturnBill is not { } bill)return Scroll(panel);
        if(_returnBillId!=bill.Id)
        {
            _returnBillId=bill.Id;_returnLines=bill.Lines.Select(l=>new ReturnEditorLine{Original=l}).ToList();
            _returnDraft=null;_returnPreview=null;_returnOperation=Guid.NewGuid();
        }
        panel.Children.Add(new TextBlock{Text=bill.Number,FontSize=24});
        var table=new DataGrid{ItemsSource=_returnLines,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,Height=220};
        AutomationProperties.SetAutomationId(table,"ReturnLines");
        foreach(var (label,path) in new[]{("ReceiptName","Original.Name"),("ReturnRemaining","Original.Remaining"),("ReturnCreditRemaining","Original.CreditRemaining")})
            table.Columns.Add(new DataGridTextColumn{Header=Texts.Get(label),Binding=new Binding(path){Mode=BindingMode.OneWay},IsReadOnly=true});
        table.Columns.Add(new DataGridTextColumn{Header=Texts.Get("ReturnQuantity"),Binding=new Binding("Requested"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
        table.Columns.Add(new DataGridComboBoxColumn{Header=Texts.Get("ReturnDisposition"),SelectedValueBinding=new Binding("Disposition"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
            SelectedValuePath="Value",DisplayMemberPath="Name",ItemsSource=new[]{new{Value=ReturnDisposition.Saleable,Name=Texts.Get("DispositionSaleable")},new{Value=ReturnDisposition.Damaged,Name=Texts.Get("DispositionDamaged")}}});
        table.Columns.Add(new DataGridTextColumn{Header=Texts.Get("Unit"),Binding=new Binding("Original.Unit"){Mode=BindingMode.OneWay,Converter=new EnumTextConverter("Unit")},IsReadOnly=true});
        panel.Children.Add(WithEmptyState(table));
        var reason=Field(panel,"ReturnReason",_returnDraft?.Reason);
        var useBasket=new CheckBox{Content=Texts.Get("ExchangeUseBasket"),IsChecked=_returnDraft?.UseBasket==true};AutomationProperties.SetAutomationId(useBasket,"ExchangeUseBasket");panel.Children.Add(useBasket);
        var isVoid=new CheckBox{Content=Texts.Get("VoidOriginalBill"),IsChecked=_returnDraft?.Void==true};AutomationProperties.SetAutomationId(isVoid,"VoidOriginalBill");panel.Children.Add(isVoid);
        var method=new ComboBox{ItemsSource=new[]{Texts.Get("TenderCash"),Texts.Get("TenderCard")},SelectedIndex=_returnDraft?.CollectionMethod==PaymentMethod.Card?1:0};panel.Children.Add(method);
        var tender=Field(panel,"ReturnCashTendered",Money.FromMinor(_returnDraft?.TenderedMinor??0,Currency.FromCode(bill.Currency)).ToDecimal().ToString(CultureInfo.CurrentCulture));
        var collectionCard=new CheckBox{Content=Texts.Get("ExternalCardApproved"),IsChecked=_returnDraft?.CollectionCardApproved==true};panel.Children.Add(collectionCard);
        var refundCard=new CheckBox{Content=Texts.Get("ExternalCardRefundApproved"),IsChecked=_returnDraft?.RefundCardApproved==true};panel.Children.Add(refundCard);
        var cardReference=Field(panel,"ExternalCardReference",_returnDraft?.CardReference);
        var negative=new CheckBox{Content=Texts.Get("AllowNegativeStock"),IsChecked=_returnDraft?.AllowNegative==true};panel.Children.Add(negative);
        var negativeReason=Field(panel,"NegativeStockReason",_returnDraft?.NegativeReason);
        ReturnDraft Read()
        {
            table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);
            try
            {
                if(!decimal.TryParse(tender.Text,out var cash))throw new FoundationException("InvalidTender");
                var items=_returnLines.Where(l=>l.Requested!=0).Select(l=>new ReturnSelection(l.Original.Id,Quantity.Of(l.Requested).Thousandths,l.Disposition)).ToArray();
                return new(_returnOperation,bill.Id,items,reason.Text,useBasket.IsChecked==true,isVoid.IsChecked==true,method.SelectedIndex==0?PaymentMethod.Cash:PaymentMethod.Card,
                    Money.FromMajor(cash,Currency.FromCode(bill.Currency)).MinorUnits,collectionCard.IsChecked==true,refundCard.IsChecked==true,cardReference.Text,
                    negative.IsChecked==true,negativeReason.Text);
            }
            catch(RetailCore.Domain.Common.DomainException){throw new FoundationException("InvalidReturnQuantity");}
            catch(OverflowException){throw new FoundationException("InvalidReturnQuantity");}
        }
        panel.Children.Add(Button("EditReplacementBasket",()=>{_returnDraft=Read();return _vm.NavigateAsync("Billing");}));
        panel.Children.Add(Button("PreviewReturn",async()=>{_returnDraft=Read();_returnPreview=await _vm.ReturnsService.PreviewAsync(_returnDraft);Render();}));
        string Amount(long minor)=>ReceiptLayout.Amount(minor,bill.Currency);
        if(_returnPreview is { } preview)
        {
            var summary=$"{Texts.Get("ReturnCredit")}: {Amount(preview.CreditMinor)} · {Texts.Get("ReplacementTotal")}: {Amount(preview.ReplacementMinor)}\n"
                +$"{Texts.Get("ReturnCollect")}: {Amount(preview.DueMinor)} · {Texts.Get("CashRounding")}: {Amount(preview.RoundingMinor)}\n"
                +$"{Texts.Get("ReturnCashPayout")}: {Amount(preview.CashPayoutMinor)} · {Texts.Get("ReturnCardPayout")}: {Amount(preview.CardPayoutMinor)}\n"
                +$"{Texts.Get("CashChange")}: {Amount(_returnDraft?.CollectionMethod==PaymentMethod.Cash && preview.ReplacementMinor>preview.CreditMinor ? Math.Max(0,(_returnDraft?.TenderedMinor??0)-preview.DueMinor):0)}\n"
                +Texts.Get(preview.NegativeStock?"InsufficientStock":"ReturnReviewHelp");
            summary+="\n"+string.Join('\n',preview.Stock.Select(s=>$"{s.Name}: {Texts.Get("StockBefore")}: {s.Before:0.###} · {Texts.Get("ReturnQuantity")}: {s.Returned:0.###} · {Texts.Get("StockConsumed")}: {s.Consumed:0.###} · {Texts.Get("StockAfter")}: {s.After:0.###} {Texts.Get("Unit"+s.Unit)}"));
            panel.Children.Add(new TextBlock{Text=summary,TextWrapping=TextWrapping.Wrap,FontSize=20});
            panel.Children.Add(Button("CompleteReturn",async()=>
            {
                var draft=Read();
                var review=bill.Number+"\n"+Texts.Get(draft.CollectionMethod==PaymentMethod.Cash?"TenderCash":"TenderCard")+"\n"+(draft.Void?Texts.Get("VoidOriginalBill"):Texts.Get("Returns"))+"\n"+summary+"\n"+string.Join('\n',_returnLines.Where(l=>draft.Void || l.Requested>0).Select(l=>$"{l.Original.Name}: {(draft.Void?l.Original.Sold:l.Requested):0.000} · {Texts.Get("Disposition"+(draft.Void?ReturnDisposition.Saleable:l.Disposition))}"))
                    +"\n"+string.Join('\n',preview.ReplacementLines.Select(l=>$"{l.ReceiptName}: {l.Quantity:0.000} × {l.ChargedUnitPrice:0.00} = {l.Total:0.00} · {l.Reason}"))
                    +"\n"+draft.Reason+"\n"+Texts.Get("ReturnCashTendered")+": "+Amount(draft.TenderedMinor)+"\n"+draft.NegativeReason;
                var dialog=new ApprovalDialog(_vm.Service,_vm.Approvers,ReturnService.Approval(draft,preview.Hash),review){Owner=this};
                if(dialog.ShowDialog()!=true)return;
                var result=await _vm.ReturnsService.CompleteAsync(draft,preview.Hash,dialog.Ticket);
                ResetReturnEditor();await _vm.RefreshReturnBillAsync();_vm.ShowSaved();
                _vm.ShowMessage("ReturnSaved");
                _=result;
            }));
        }
        Label(panel,"ReturnHistory");
        foreach(var result in bill.History)
        {
            panel.Children.Add(new TextBlock{Text=$"{result.Number} · {Texts.Get("ReplacementBill")}: {result.ReplacementNumber??"—"} · {Texts.Get("ReturnCredit")}: {Amount(result.CreditMinor)}",TextWrapping=TextWrapping.Wrap});
            panel.Children.Add(Button("PreviewReturnReceipt",async()=>
            {
                var info=await _vm.Receipts.ReturnInfoAsync(result.Id);var settings=await _vm.Receipts.SettingsAsync();
                var document=ReceiptRenderer.Render(info.Document,settings.PrintableWidthMillimeters,info.Prints.Count>0);
                var paginator=((IDocumentPaginatorSource)document).DocumentPaginator;paginator.ComputePageCount();var pages=new StackPanel();
                for(var i=0;i<paginator.PageCount;i++)pages.Children.Add(new DocumentPageView{DocumentPaginator=paginator,PageNumber=i});
                new Window{Owner=this,Title=Texts.Get("PreviewReturnReceipt"),Width=550,Height=720,Content=new ScrollViewer{Content=pages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}}.ShowDialog();
            }));
            panel.Children.Add(Button("ReturnPrintHistory",async()=>
            {
                var info=await _vm.Receipts.ReturnInfoAsync(result.Id);
                var history=new StackPanel();history.Children.Add(new TextBlock{Text=Texts.Get("PrintRecoveryHelp"),TextWrapping=TextWrapping.Wrap});
                history.Children.Add(StockTable(info.Prints.Select(p=>new {At=p.Attempt.RequestedAt.ToString("u"),p.Attempt.Printer,
                    Copy=Texts.Get(p.Attempt.IsCopy?"ReprintReceipt":"PrintReceipt"),Outcome=Texts.Get(p.Outcome?.Status??"PrintUnknown")}),
                    "ReturnPrintHistoryTable",("PrintDateUtc","At",null),("ReceiptPrinter","Printer",null),("PrintCopy","Copy",null),("PrintStatus","Outcome",null)));
                new Window{Owner=this,Title=Texts.Get("ReturnPrintHistory"),Width=850,Height=400,Content=new ScrollViewer{Content=history}}.ShowDialog();
            }));
            panel.Children.Add(Button("PrintReturnReceipt",async()=>
            {
                var printed=await _vm.Receipts.PrintReturnAsync(result.Id,Guid.NewGuid(),new WindowsReceiptPrinter(Dispatcher));_vm.ShowMessage(printed.Status);
            }));
        }
        ArrangeFields(panel);
        panel.Loaded+=(_,_)=>lookup.Focus();return Scroll(panel);
    }
}
