using System.Globalization;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;

namespace RetailCore.Application.Security;

/// <summary>SEC-06: a void of a completed sale is always recorded.</summary>
public sealed class AuditSaleVoided(IAuditLog auditLog) : IDomainEventHandler<SaleVoided>
{
    public Task HandleAsync(SaleVoided domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        auditLog.Record(AuditEntry.Record(
            domainEvent.ShopId,
            AuditAction.SaleVoided,
            nameof(Sale),
            domainEvent.SaleId.Value,
            domainEvent.OccurredAt,
            authorisedBy: domainEvent.AuthorisedBy,
            terminalId: domainEvent.TerminalId,
            summary: string.Create(
                CultureInfo.InvariantCulture,
                $"Receipt {domainEvent.ReceiptNumber} for {domainEvent.Total} voided: {domainEvent.Reason}")));

        return Task.CompletedTask;
    }
}

/// <summary>SEC-06: a line taken off before completion is recorded too.</summary>
public sealed class AuditSaleLineVoided(IAuditLog auditLog) : IDomainEventHandler<SaleLineVoided>
{
    public Task HandleAsync(SaleLineVoided domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        auditLog.Record(AuditEntry.Record(
            domainEvent.ShopId,
            AuditAction.SaleLineVoided,
            nameof(SaleLine),
            domainEvent.SaleLineId.Value,
            domainEvent.OccurredAt,
            performedBy: domainEvent.VoidedBy,
            summary: string.Create(
                CultureInfo.InvariantCulture,
                $"'{domainEvent.Description}' at {domainEvent.Amount} removed: {domainEvent.Reason}")));

        return Task.CompletedTask;
    }
}

/// <summary>
/// SEC-06: every discount is recorded, whether or not it needed approval.
/// </summary>
/// <remarks>
/// Recording only the ones that crossed the threshold would miss the pattern that actually
/// matters, which is a steady drip of small unapproved discounts on the same till.
/// </remarks>
public sealed class AuditDiscountApplied(IAuditLog auditLog) : IDomainEventHandler<DiscountApplied>
{
    public Task HandleAsync(DiscountApplied domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        auditLog.Record(AuditEntry.Record(
            domainEvent.ShopId,
            AuditAction.DiscountGiven,
            domainEvent.SaleLineId is null ? nameof(Sale) : nameof(SaleLine),
            domainEvent.SaleLineId?.Value ?? domainEvent.SaleId.Value,
            domainEvent.OccurredAt,
            authorisedBy: domainEvent.AuthorisedBy,
            summary: string.Create(
                CultureInfo.InvariantCulture,
                $"Discount of {domainEvent.Amount}: {domainEvent.Reason}")));

        return Task.CompletedTask;
    }
}

/// <summary>SEC-06 and RET-05.</summary>
public sealed class AuditRefundIssued(IAuditLog auditLog) : IDomainEventHandler<RefundIssued>
{
    public Task HandleAsync(RefundIssued domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        auditLog.Record(AuditEntry.Record(
            domainEvent.ShopId,
            AuditAction.RefundIssued,
            nameof(Refund),
            domainEvent.RefundId.Value,
            domainEvent.OccurredAt,
            performedBy: domainEvent.CashierId,
            authorisedBy: domainEvent.AuthorisedBy,
            terminalId: domainEvent.TerminalId,
            summary: string.Create(
                CultureInfo.InvariantCulture,
                $"Refund of {domainEvent.Total}: {domainEvent.Reason}")));

        return Task.CompletedTask;
    }
}
