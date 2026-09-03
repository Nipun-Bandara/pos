using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// The next receipt number for one terminal. SYN-03.
/// </summary>
/// <remarks>
/// One row per terminal. Incremented inside the transaction that writes the sale, under the
/// row lock the update itself takes, so two concurrent sales on the same till cannot get the
/// same number and a rollback cannot leave a gap.
/// <para>
/// A per-shop counter would need the tills to agree with each other, which is exactly what
/// they cannot do when the network is down. Per terminal, each till owns its own sequence
/// and never has to ask anyone.
/// </para>
/// </remarks>
public sealed class ReceiptCounter
{
    private ReceiptCounter(ShopId shopId, TerminalId terminalId, string terminalCode)
    {
        ShopId = shopId;
        TerminalId = terminalId;
        TerminalCode = terminalCode;
        LastIssued = 0;
    }

    private ReceiptCounter() => TerminalCode = null!;

    public TerminalId TerminalId { get; private set; }

    public ShopId ShopId { get; private set; }

    /// <summary>Prefixes the printed number, for example "T1".</summary>
    public string TerminalCode { get; private set; }

    /// <summary>The last number handed out. Zero before the first sale.</summary>
    public long LastIssued { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, so a lost update cannot duplicate a number.
    /// </summary>
    /// <remarks>
    /// Incremented here rather than left to the provider. PostgreSQL has <c>xmin</c> and
    /// SQLite has nothing equivalent, so a provider-generated token would behave differently
    /// on the till and on the server. Owning the increment keeps one model working on both,
    /// which is the whole point of Phase 1.
    /// </remarks>
    public long Version { get; private set; }

    public static ReceiptCounter For(ShopId shopId, TerminalId terminalId, string terminalCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalCode);

        return new ReceiptCounter(shopId, terminalId, terminalCode.Trim().ToUpperInvariant());
    }

    /// <summary>Takes the next number in the sequence.</summary>
    public ReceiptNumber Next()
    {
        LastIssued++;
        Version++;
        return ReceiptNumber.Create(TerminalCode, LastIssued);
    }
}
