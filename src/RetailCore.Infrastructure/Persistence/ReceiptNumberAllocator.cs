using Microsoft.EntityFrameworkCore;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// Hands out gapless per-terminal receipt numbers. SYN-03.
/// </summary>
/// <remarks>
/// Call this inside the transaction that writes the sale. The counter row is tracked and
/// incremented, so the increment commits with the sale or rolls back with it, and a crash
/// between the two cannot leave a hole in the sequence. The row version on the counter turns
/// a lost update into a concurrency exception rather than two sales sharing a number.
/// </remarks>
public sealed class ReceiptNumberAllocator(RetailCoreDbContext context) : IReceiptNumberAllocator
{
    public async Task<ReceiptNumber> NextAsync(
        ShopId shopId,
        TerminalId terminalId,
        CancellationToken cancellationToken = default)
    {
        var counter = context.ReceiptCounters.Local.FirstOrDefault(c => c.TerminalId == terminalId) ?? await context.ReceiptCounters
            .FirstOrDefaultAsync(c => c.TerminalId == terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (counter is null)
        {
            var terminal = await context.Terminals
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == terminalId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Terminal {terminalId} is not provisioned, so it cannot issue receipts.");

            counter = ReceiptCounter.For(shopId, terminalId, terminal.Code);
            context.ReceiptCounters.Add(counter);
        }

        return counter.Next();
    }
}
