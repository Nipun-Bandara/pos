using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Checkout;
using RetailCore.Application.Foundation;
using RetailCore.Application.Inventory;
using RetailCore.Application.Receipts;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Application.Returns;

public sealed class ReturnService(ILocalTransactions transactions, FoundationService foundation, LocalSession session,
    BillingService billing, ReceiptService receipts, IClock clock)
{
    private sealed record Review(Shop Shop, User Actor, Sale Original, Shift Shift, IReadOnlyList<Refund> Past,
        Refund Refund, BillingState Basket, Sale? Replacement, ReturnPreview Preview);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static ApprovalRequest Approval(ReturnDraft draft, string hash) => new("ReturnExchange",JsonSerializer.Serialize(new { Request=Hash(draft), Preview=hash }));

    public async Task<ReturnBill> FindAsync(string number, CancellationToken ct = default) =>
        await LoadAsync((await receipts.FindAsync(number,ct).ConfigureAwait(false)).Receipt.SaleId,ct).ConfigureAwait(false);
    public Task<ReturnBill> LoadAsync(Guid id, CancellationToken ct = default) => transactions.ReadAsync(async (store, token) =>
    {
        var actor=await foundation.RequireUserAsync(store,false,token).ConfigureAwait(false);
        var sale=await store.SavedSaleAsync(actor.ShopId,id,null,token).ConfigureAwait(false) ?? throw new FoundationException("BillNotFound");
        var past=await store.RefundsAsync(actor.ShopId,sale.Id,token).ConfigureAwait(false);
        var caps=ReturnAmounts.Entitlements(sale);
        var lines=sale.ActiveLines.Select(l => new ReturnLineView(l.Id,l.ReceiptNameSnapshot ?? l.Description,l.Quantity.Value,
            l.Quantity.Value-past.SelectMany(r=>r.Lines).Where(r=>r.OriginalSaleLineId==l.Id).Sum(r=>r.Quantity.Value),
            (caps[l.Id]-past.SelectMany(r=>r.Lines).Where(r=>r.OriginalSaleLineId==l.Id).Sum(r=>r.Amount.MinorUnits))/(decimal)sale.Currency.MinorUnitsPerMajor,
            l.Unit.ToString())).ToArray();
        return new ReturnBill(id,sale.ReceiptNumber!.Value.ToString(),sale.CurrencyCode,lines,await store.ReturnsAsync(actor.ShopId,id,token).ConfigureAwait(false));
    },ct);

    public Task<ReturnPreview> PreviewAsync(ReturnDraft draft, CancellationToken ct = default) => transactions.WriteAsync(async (store,token) =>
        (await ReviewAsync(store,draft,null,token).ConfigureAwait(false)).Preview,ct);

    public Task<ReturnResult> CompleteAsync(ReturnDraft draft, string expectedHash, ApprovalTicket? ticket, CancellationToken ct = default)
    {
        draft=draft with { Items=draft.Items.ToArray() };
        var version=session.Version;
        var approve=foundation.ApprovalValidator(ticket,Approval(draft,expectedHash));
        return transactions.WriteAsync(async (store,token) =>
        {
            var actor=await foundation.RequireUserAsync(store,false,token).ConfigureAwait(false);
            if (session.Version!=version) throw new FoundationException("SignInRequired");
            var fingerprint=Hash(new { Actor=actor.Id,Draft=draft,Hash=expectedHash });
            var prior=await store.CommandAsync(draft.OperationId,token).ConfigureAwait(false);
            if (prior is not null)
            {
                if(prior.Kind!="ReturnExchange" || prior.Fingerprint!=fingerprint) throw new FoundationException("OperationConflict");
                return JsonSerializer.Deserialize<ReturnResult>(prior.Result)!;
            }
            var review=await ReviewAsync(store,draft,null,token).ConfigureAwait(false);
            if(review.Preview.Hash!=expectedHash) throw new FoundationException("ReturnChanged");
            var p=review.Preview;
            if(p.NegativeStock && (!draft.AllowNegative || string.IsNullOrWhiteSpace(draft.NegativeReason))) throw new FoundationException("InsufficientStock");
            if(p.CardPayoutMinor>0 && !draft.RefundCardApproved) throw new FoundationException("CardRefundApprovalRequired");
            if(p.DueMinor>0 && draft.CollectionMethod==PaymentMethod.Card && !draft.CollectionCardApproved) throw new FoundationException("CardApprovalRequired");
            if(p.DueMinor>0 && draft.CollectionMethod==PaymentMethod.Cash && draft.TenderedMinor<p.DueMinor) throw new FoundationException("InsufficientTender");
            var owner=await approve(store,actor,token).ConfigureAwait(false);
            // Rebuild the actual aggregates with the authenticated owner, rather than persisting preview authority.
            review=await ReviewAsync(store,draft,owner,token).ConfigureAwait(false);
            var refund=review.Refund; var replacement=review.Replacement;
            long collected=0,rounding=0,change=0;
            if(replacement is not null)
            {
                if(p.AppliedCreditMinor>0) replacement.ApplyExchangeCredit(refund.Id,p.CreditCashUsed,p.CreditCardUsed,clock.UtcNow);
                if(replacement.BalanceDue.IsPositive)
                {
                    if(draft.CollectionMethod==PaymentMethod.Cash) replacement.AddCashPayment(Money.FromMinor(draft.TenderedMinor,review.Shop.Currency),clock.UtcNow,review.Shop.CashIncrementMinor);
                    else replacement.AddPayment(PaymentMethod.Card,replacement.BalanceDue,draft.CardReference,clock.UtcNow);
                }
                collected=replacement.Payments.Where(x=>x.Method!=PaymentMethod.ExchangeCredit).Sum(x=>x.Amount.MinorUnits);
                rounding=replacement.CashRoundingAdjustment.MinorUnits;change=replacement.ChangeGiven.MinorUnits;
            }
            var actualCashTendered=replacement?.Payments.Where(x=>x.Method==PaymentMethod.Cash).Sum(x=>x.Tendered.MinorUnits)??0;
            var incomingCash=replacement?.Payments.Where(x=>x.Method==PaymentMethod.Cash).Sum(x=>x.Amount.MinorUnits) ?? 0;
            var drawer=(await store.ExpectedCashAsync(review.Shift,token).ConfigureAwait(false)).MinorUnits;
            try { if(checked(drawer+incomingCash-p.CashPayoutMinor)<0) throw new FoundationException("InsufficientDrawer"); }
            catch(OverflowException) { throw new FoundationException("BillingAmountTooLarge"); }
            refund.SetSettlement(p.CashCreditMinor,p.CardCreditMinor,p.CashPayoutMinor,p.CardPayoutMinor,replacement?.Id,draft.CardReference);
            var number=await store.NextReceiptAsync(review.Shop.Id,review.Shift.TerminalId,token).ConfigureAwait(false);
            if(draft.Void)
            {
                review.Original.Void(draft.Reason,owner,clock.UtcNow,review.Past.Count>0);
                refund.CompleteVoidReversal(review.Original,number,clock.UtcNow);
            }
            else refund.Complete(number,clock.UtcNow);
            // Prepost returns before replacement consumption; saleable pack/loose quantities share the same ledger.
            var returned=draft.Void ? Array.Empty<SoldItem>() : RefundItems(refund);
            await store.PostStockAsync(review.Shop.Id,"Refund",refund.Id.Value,StockPosting.Movements(review.Shop.Id,"Refund",refund.Id.Value,
                returned,StockDirection.In,StockMovementKind.Refund,clock.UtcNow,actor.Id,review.Shop.Currency,draft.Reason,owner),token).ConfigureAwait(false);
            if(draft.Void)
                await store.PostStockAsync(review.Shop.Id,"SaleVoid",review.Original.Id.Value,StockPosting.Movements(review.Shop.Id,"SaleVoid",review.Original.Id.Value,
                    review.Original.ItemsSold(),StockDirection.In,StockMovementKind.SaleVoided,clock.UtcNow,actor.Id,review.Shop.Currency,draft.Reason,owner),token).ConfigureAwait(false);
            store.Add(refund);
            if(replacement is not null)
            {
                replacement.Complete(await store.NextReceiptAsync(review.Shop.Id,review.Shift.TerminalId,token).ConfigureAwait(false),clock.UtcNow);
                await store.PostStockAsync(review.Shop.Id,"Sale",replacement.Id.Value,StockPosting.Movements(review.Shop.Id,"Sale",replacement.Id.Value,
                    replacement.ItemsSold(),StockDirection.Out,StockMovementKind.Sale,clock.UtcNow,actor.Id,review.Shop.Currency,
                    p.NegativeStock?draft.NegativeReason!:"Sale",p.NegativeStock?owner:null),token).ConfigureAwait(false);
                foreach(var line in replacement.Lines.Where(l=>l.ManualPriceApprovedBy is not null))
                    store.Audit(AuditEntry.Record(review.Shop.Id,AuditAction.PriceOverridden,"Sale",replacement.Id.Value,clock.UtcNow,actor.Id,owner,
                        detailsJson:JsonSerializer.Serialize(new {line.ProductId,line.Quantity,line.UnitPrice,line.QualifiedTierUnitPriceMinor,line.ManualPriceReason})));
                if(p.NegativeStock) store.Audit(AuditEntry.Record(review.Shop.Id,AuditAction.NegativeStockApproved,"Sale",replacement.Id.Value,clock.UtcNow,actor.Id,owner,
                    detailsJson:JsonSerializer.Serialize(new {draft.NegativeReason,review.Basket.Quote.Stock})));
                store.Add(replacement);store.AddReceipt(review.Shop.Id,ReceiptDocument.Capture(replacement,review.Shop,actor.DisplayName));
                store.SaveBasket(review.Shop.Id,review.Shift.TerminalId,actor.Id,BasketDocument.Empty());
            }
            var doc=ReceiptDocument.ForReturn(draft.OperationId,number.ToString(),review.Original,review.Shop,actor.DisplayName,clock.UtcNow,refund,replacement,collected,rounding,actualCashTendered,change);
            var result=new ReturnResult(draft.OperationId,draft.OriginalSaleId,refund.Id.Value,replacement?.Id.Value,number.ToString(),replacement?.ReceiptNumber?.ToString(),
                refund.Total.MinorUnits,p.AppliedCreditMinor,collected,p.CashPayoutMinor,p.CardPayoutMinor,rounding,actualCashTendered,change,doc,draft.Void);
            store.AddReturn(review.Shop.Id,result);
            store.Audit(AuditEntry.Record(review.Shop.Id,AuditAction.ReturnCompleted,"Return",draft.OperationId,clock.UtcNow,actor.Id,owner,
                detailsJson:JsonSerializer.Serialize(new { result.Number,result.OriginalSaleId,result.ReplacementSaleId,result.CreditMinor,result.CollectedMinor,result.CashPayoutMinor,result.CardPayoutMinor,draft.Reason,p.NegativeStock })));
            store.CompleteCommand(draft.OperationId,"ReturnExchange",fingerprint,JsonSerializer.Serialize(result));
            await foundation.RequireUserAsync(store,false,token).ConfigureAwait(false);
            if(session.Version!=version) throw new FoundationException("SignInRequired");
            return result;
        },ct);
    }

    private async Task<Review> ReviewAsync(ILocalStore store,ReturnDraft d,UserId? owner,CancellationToken ct)
    {
        var actor=await foundation.RequireUserAsync(store,false,ct).ConfigureAwait(false);
        if(d.OperationId==Guid.Empty || string.IsNullOrWhiteSpace(d.Reason) || d.Reason.Length>500 || d.Items.Count>200
            || d.TenderedMinor<0 || d.CollectionMethod is not (PaymentMethod.Cash or PaymentMethod.Card)
            || !ExternalCardReference.IsValid(d.CardReference) || d.NegativeReason?.Length>500) throw new FoundationException("InvalidReturn");
        var shop=(await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
        var original=await store.SavedSaleAsync(shop.Id,d.OriginalSaleId,null,ct).ConfigureAwait(false) ?? throw new FoundationException("BillNotFound");
        if(original.Status!=SaleStatus.Completed || original.CurrencyCode!=shop.Currency.Code) throw new FoundationException("ReturnNotEligible");
        var state=await billing.WithinAsync(store,ct).ConfigureAwait(false);
        var shift=await store.OpenShiftAsync(shop.Id,(await store.TerminalsAsync(shop.Id,ct).ConfigureAwait(false)).Single(t=>t.IsActive).Id,ct).ConfigureAwait(false)
            ?? throw new FoundationException("OpenShiftRequired");
        var past=await store.RefundsAsync(shop.Id,original.Id,ct).ConfigureAwait(false);
        if(d.Void && (d.UseBasket || past.Count>0 || original.ShiftId!=shift.Id)) throw new FoundationException("VoidNotEligible");
        var quantities=past.SelectMany(r=>r.Lines).GroupBy(l=>l.OriginalSaleLineId).ToDictionary(g=>g.Key,g=>Quantity.FromThousandths(g.Sum(l=>l.Quantity.Thousandths)));
        var amounts=past.SelectMany(r=>r.Lines).GroupBy(l=>l.OriginalSaleLineId).ToDictionary(g=>g.Key,g=>g.Sum(l=>l.Amount.MinorUnits));
        Refund refund;
        try
        {
            refund=Refund.Against(original,shift.Id,actor.Id,d.Reason,owner??actor.Id,PaymentMethod.Cash,clock.UtcNow,quantities,new RefundId(d.OperationId),amounts);
            var items=d.Void ? original.ActiveLines.Select(l=>new ReturnSelection(l.Id,l.Quantity.Thousandths,ReturnDisposition.Saleable)).ToArray() : d.Items;
            foreach(var item in items) refund.AddLine(item.LineId,Quantity.FromThousandths(item.Thousandths),item.Disposition);
            if(refund.Lines.Count==0) throw new FoundationException("SelectReturnItems");
        }
        catch(DomainException) { throw new FoundationException("InvalidReturnQuantity"); }
        if(original.Payments.Any(p=>p.Method is not (PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.ExchangeCredit))
            || past.Any(r=>r.CashCreditMinor is null && r.Method is not (PaymentMethod.Cash or PaymentMethod.Card))) throw new FoundationException("UnsupportedOriginalTender");
        var cash=checked(original.Payments.Where(p=>p.Method==PaymentMethod.Cash).Sum(p=>p.Amount.MinorUnits)+original.ExchangeCashCreditMinor
            -past.Sum(r=>r.CashCreditMinor ?? (r.Method==PaymentMethod.Cash?r.Total.MinorUnits:0)));
        var card=checked(original.Payments.Where(p=>p.Method==PaymentMethod.Card).Sum(p=>p.Amount.MinorUnits)+original.ExchangeCardCreditMinor
            -past.Sum(r=>r.CardCreditMinor ?? (r.Method==PaymentMethod.Card?r.Total.MinorUnits:0)));
        if(cash<0 || card<0 || refund.Total.MinorUnits>checked(cash+card)) throw new FoundationException("InvalidReturnHistory");
        var cashCredit=Math.Min(cash,refund.Total.MinorUnits);var cardCredit=refund.Total.MinorUnits-cashCredit;
        Sale? replacement=null;
        if(d.UseBasket)
        {
            if(state.Quote.ErrorKey is { } error) throw new FoundationException(error);
            if(state.Quote.Lines.Count==0) throw new FoundationException("BasketEmpty");
            replacement=await billing.BuildWithinAsync(store,owner??actor.Id,ct).ConfigureAwait(false);
        }
        var replacementTotal=replacement?.Total.MinorUnits ?? 0;
        var creditUsed=Math.Min(refund.Total.MinorUnits,replacementTotal);
        var cashUsed=Math.Min(cashCredit,creditUsed);var cardUsed=creditUsed-cashUsed;
        var difference=Math.Max(0,replacementTotal-refund.Total.MinorUnits);
        var due=d.CollectionMethod==PaymentMethod.Cash ? Money.FromMinor(difference,shop.Currency).RoundToIncrement(shop.CashIncrementMinor).MinorUnits : difference;
        var saleable=RefundItems(refund).Where(i=>i.Saleable).GroupBy(i=>i.StockProductId??i.ProductId).ToDictionary(g=>g.Key,g=>g.Sum(i=>StockConversion.ToBase(i.Quantity,i.FactorThousandths).Thousandths));
        var stock=d.UseBasket ? state.Quote.Stock.Select(s=>
        {
            var root=state.Products.Single(p=>p.Id==s.ProductId);
            var returned=saleable.GetValueOrDefault(s.ProductId)/1000m;
            return new ReturnStock(root.SinhalaName,root.Unit.ToString(),s.Available,returned,s.Required,s.Remaining+returned);
        }).ToArray():[];
        var negative=stock.Any(s=>s.After<0);
        var hash=Hash(new { Draft=d,Original=original.Id,original.Status,Prior=past.Select(r=>new { r.Id,r.Total,r.CashCreditMinor,r.CardCreditMinor }).OrderBy(r=>r.Id.Value),
            Shift=shift.Id, Basket=d.UseBasket?state.Quote.Hash:null, Credit=refund.Total.MinorUnits,Cash=cashCredit,Card=cardCredit,Replacement=replacementTotal,Due=due,Negative=negative });
        return new(shop,actor,original,shift,past,refund,state,replacement,new(hash,refund.Total.MinorUnits,replacementTotal,creditUsed,due,due-difference,
            cashCredit-cashUsed,cardCredit-cardUsed,cashCredit,cardCredit,cashUsed,cardUsed,shop.Currency.Code,negative,d.UseBasket?state.Quote.Lines:[],stock));
    }
    private static SoldItem[] RefundItems(Refund refund) => refund.Lines.Select(l=>new SoldItem(l.ProductId,l.Quantity,l.StockProductId,l.StockFactorThousandths,
        l.StockBaseUnit??RetailCore.Domain.Catalogue.UnitOfMeasure.Each,Money.FromMinor(l.UnitCostMinor,refund.Currency),l.Disposition==ReturnDisposition.Saleable)).ToArray();
}
