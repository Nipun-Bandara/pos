using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// A change waiting to be sent to the server. SYN-04.
/// </summary>
/// <remarks>
/// <para>
/// Written in the same transaction as the change it describes. That is the whole point: if
/// the sale committed, the outbox row committed, so there is no state in which a till has
/// taken money the server will never hear about. PLAN.md rates lost sales as a severe risk,
/// and this is the mechanism that removes it.
/// </para>
/// <para>
/// Rows are never deleted until the server acknowledges them, and delivery is at least once,
/// so the server ingest has to be idempotent (SYN-05). Duplicate delivery is cheap to handle
/// on the server; lost delivery is not recoverable at all.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    private OutboxMessage(
        Guid id,
        ShopId shopId,
        TerminalId terminalId,
        string messageType,
        string payload,
        DateTimeOffset createdAt)
    {
        Id = id;
        ShopId = shopId;
        TerminalId = terminalId;
        MessageType = messageType;
        Payload = payload;
        CreatedAt = createdAt;
    }

    private OutboxMessage()
    {
        MessageType = null!;
        Payload = null!;
    }

    /// <summary>UUIDv7, so the outbox drains in roughly the order it filled. SYN-02.</summary>
    public Guid Id { get; private set; }

    public ShopId ShopId { get; private set; }

    public TerminalId TerminalId { get; private set; }

    /// <summary>The event's type name, used to route it on the server.</summary>
    public string MessageType { get; private set; }

    /// <summary>The serialised event.</summary>
    public string Payload { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set once the server has acknowledged it. Null while it is still pending.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>How many times delivery has been tried, for the backoff in Phase 4.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>The last failure, kept so a stuck till can be diagnosed remotely. OPS-05.</summary>
    public string? LastError { get; private set; }

    public bool IsPending => DispatchedAt is null;

    public static OutboxMessage For(
        ShopId shopId,
        TerminalId terminalId,
        string messageType,
        string payload,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(payload);

        return new OutboxMessage(Guid.CreateVersion7(), shopId, terminalId, messageType, payload, createdAt);
    }

    public void MarkDispatched(DateTimeOffset at) => DispatchedAt = at;

    public void RecordFailure(string error, DateTimeOffset at)
    {
        AttemptCount++;
        LastError = error;
        _ = at;
    }
}
