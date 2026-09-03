namespace RetailCore.Domain.Common;

/// <summary>
/// Marker for the strongly typed identifiers below, so that EF Core configuration and
/// the architecture tests can find them all without reflection guesswork.
/// </summary>
public interface IEntityId
{
    Guid Value { get; }
}

/*
 * SYN-02: identifiers are generated on the client, never by the database.
 *
 * A till that is offline still has to mint primary keys, and those keys have to be
 * unique across every terminal in the estate without coordination. UUIDv7 gives that,
 * and because the first 48 bits are a millisecond timestamp the values stay sortable,
 * which keeps B-tree index inserts sequential instead of scattering them. A v4 GUID
 * would work for uniqueness but would fragment every index in the system.
 *
 * These are separate types rather than bare Guids so that passing a ProductId where a
 * SaleId belongs is a compile error rather than a support call.
 */

public readonly record struct ShopId(Guid Value) : IEntityId
{
    public static ShopId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct TerminalId(Guid Value) : IEntityId
{
    public static TerminalId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct UserId(Guid Value) : IEntityId
{
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ProductId(Guid Value) : IEntityId
{
    public static ProductId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct CategoryId(Guid Value) : IEntityId
{
    public static CategoryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct TaxRateId(Guid Value) : IEntityId
{
    public static TaxRateId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SaleId(Guid Value) : IEntityId
{
    public static SaleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SaleLineId(Guid Value) : IEntityId
{
    public static SaleLineId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PaymentId(Guid Value) : IEntityId
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RefundId(Guid Value) : IEntityId
{
    public static RefundId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct RefundLineId(Guid Value) : IEntityId
{
    public static RefundLineId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ShiftId(Guid Value) : IEntityId
{
    public static ShiftId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct CashMovementId(Guid Value) : IEntityId
{
    public static CashMovementId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct StockMovementId(Guid Value) : IEntityId
{
    public static StockMovementId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct AuditEntryId(Guid Value) : IEntityId
{
    public static AuditEntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
