using System.Text.Json;
using RetailCore.Application.Foundation;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
namespace RetailCore.Application.Reporting;

public sealed class CashService(ILocalTransactions transactions, FoundationService foundation, LocalSession session, IClock clock)
{
    public static ApprovalRequest Approval(CashRequest request) => new("CashReconciliation", JsonSerializer.Serialize(request));
    public Task<CashReview> ReviewAsync(CancellationToken cancellationToken = default) => transactions.ReadAsync(async (store, ct) =>
    {
        await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
        var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
        var terminal = (await store.TerminalsAsync(shop.Id, ct).ConfigureAwait(false)).Single(t => t.IsActive);
        var shift = await store.OpenShiftAsync(shop.Id, terminal.Id, ct).ConfigureAwait(false) ?? throw new FoundationException("OpenShiftRequired");
        return new CashReview(shift.Id.Value, (await store.ExpectedCashAsync(shift, ct).ConfigureAwait(false)).MinorUnits, shop.Currency.Code);
    }, cancellationToken);

    public Task<CashResult> PostAsync(CashRequest request, ApprovalTicket? ticket, CancellationToken cancellationToken = default)
    {
        var approve = foundation.ApprovalValidator(ticket, Approval(request)); var version = session.Version;
        return transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            if (session.Version != version) throw new FoundationException("SignInRequired");
            if (request.Id == Guid.Empty || request.Minor < 0 || (!request.Close && request.Minor == 0) || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
                throw new FoundationException("InvalidCashCommand");
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor.Id, request }))));
            var prior = await store.CommandAsync(request.Id, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.Kind != "CashReconciliation" || prior.Fingerprint != fingerprint) throw new FoundationException("OperationConflict");
                return JsonSerializer.Deserialize<CashResult>(prior.Result)!;
            }
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var terminal = (await store.TerminalsAsync(shop.Id, ct).ConfigureAwait(false)).Single(t => t.IsActive);
            var shift = await store.OpenShiftAsync(shop.Id, terminal.Id, ct).ConfigureAwait(false) ?? throw new FoundationException("OpenShiftRequired");
            var summary = await store.ShiftSummaryAsync(shift.Id, ct).ConfigureAwait(false);
            var expected = shift.ExpectedCashFrom(summary);
            if (shift.Id.Value != request.ShiftId || expected.MinorUnits != request.ExpectedMinor) throw new FoundationException("CashReviewChanged");
            var owner = await approve(store, actor, ct).ConfigureAwait(false);
            var amount = Money.FromMinor(request.Minor, shop.Currency);
            if (request.Close) shift.Close(summary, amount, actor.Id, clock.UtcNow, request.Reason);
            else
            {
                if (request.PayOut && amount > expected) throw new FoundationException("InsufficientDrawerCash");
                try { _ = checked(expected.MinorUnits + (request.PayOut ? -request.Minor : request.Minor)); }
                catch (OverflowException) { throw new FoundationException("InvalidCashCommand"); }
                shift.RecordCashMovement(request.PayOut ? CashMovementKind.PayOut : CashMovementKind.PayIn, amount, request.Reason, actor.Id, clock.UtcNow);
            }
            var result = new CashResult(shift.Id.Value, request.Close ? expected.MinorUnits : shift.ExpectedCashFrom(summary).MinorUnits,
                shift.CountedCash?.MinorUnits, shift.Variance?.MinorUnits);
            store.Audit(AuditEntry.Record(shop.Id, request.Close ? AuditAction.ShiftClosed : AuditAction.CashMovementRecorded, "Shift", shift.Id.Value,
                clock.UtcNow, actor.Id, owner, terminal.Id, request.Reason, JsonSerializer.Serialize(new { request, result })));
            store.CompleteCommand(request.Id, "CashReconciliation", fingerprint, JsonSerializer.Serialize(result));
            await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            if (session.Version != version) throw new FoundationException("SignInRequired");
            return result;
        }, cancellationToken);
    }
}
