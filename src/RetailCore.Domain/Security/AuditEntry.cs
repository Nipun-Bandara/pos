using RetailCore.Domain.Common;

namespace RetailCore.Domain.Security;

/// <summary>The kinds of thing worth recording. SEC-06.</summary>
public enum AuditAction
{
    SaleVoided = 0,

    SaleLineVoided = 1,

    DiscountGiven = 2,

    PriceOverridden = 3,

    RefundIssued = 4,

    DrawerOpened = 5,

    CashMovementRecorded = 6,

    ShiftOpened = 7,

    ShiftClosed = 8,

    StockAdjusted = 9,

    UserSignedIn = 10,

    UserLockedOut = 11,

    ManagerOverrideUsed = 12,

    ProductPriceChanged = 13,

    ShopConfigured = 14,

    ShopSettingsChanged = 15,

    UserChanged = 16,

    SessionLocked = 17,

    LanguageChanged = 18,
    ProductChanged = 19,
    CategoryChanged = 20,
}

/// <summary>
/// An immutable record that something privileged happened. SEC-06.
/// </summary>
/// <remarks>
/// Write once. There is no method here that changes a field after construction, and the
/// database enforces the same thing with a trigger (CMP-04). An audit log that the
/// application can edit is not an audit log.
/// </remarks>
public sealed class AuditEntry : Entity<AuditEntryId>, IBelongsToShop
{
    private AuditEntry(
        AuditEntryId id,
        ShopId shopId,
        AuditAction action,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAt,
        UserId? performedBy,
        UserId? authorisedBy,
        TerminalId? terminalId,
        string? summary,
        string? detailsJson)
        : base(id)
    {
        ShopId = shopId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OccurredAt = occurredAt;
        PerformedBy = performedBy;
        AuthorisedBy = authorisedBy;
        TerminalId = terminalId;
        Summary = summary;
        DetailsJson = detailsJson;
    }

    private AuditEntry() => EntityType = null!;

    public ShopId ShopId { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>The aggregate the action was taken against, for example "Sale".</summary>
    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Who did it. Null for actions the system took by itself.</summary>
    public UserId? PerformedBy { get; private set; }

    /// <summary>
    /// The manager who approved it, where approval was needed. SEC-05. Kept separate from
    /// <see cref="PerformedBy"/> because "who asked" and "who allowed" are different
    /// questions and an override is only meaningful if both are recorded.
    /// </summary>
    public UserId? AuthorisedBy { get; private set; }

    public TerminalId? TerminalId { get; private set; }

    /// <summary>One line a human can read without a developer.</summary>
    public string? Summary { get; private set; }

    /// <summary>Structured detail for the audit viewer. BO-05.</summary>
    public string? DetailsJson { get; private set; }

    public static AuditEntry Record(
        ShopId shopId,
        AuditAction action,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAt,
        UserId? performedBy = null,
        UserId? authorisedBy = null,
        TerminalId? terminalId = null,
        string? summary = null,
        string? detailsJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        return new AuditEntry(
            AuditEntryId.New(),
            shopId,
            action,
            entityType,
            entityId,
            occurredAt,
            performedBy,
            authorisedBy,
            terminalId,
            summary,
            detailsJson);
    }
}
