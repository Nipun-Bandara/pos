using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;

namespace RetailCore.Application.Abstractions;

/// <summary>
/// Hands out the next receipt number for a terminal. SYN-03.
/// </summary>
/// <remarks>
/// Must be called inside the transaction that writes the sale. Allocating outside it means a
/// crash between allocating and saving burns a number, and a gap in the sequence is
/// something a tax audit expects to be explained. The implementation takes a row lock on the
/// terminal's counter, which is cheap because the counter is per terminal and a terminal
/// serves one customer at a time.
/// </remarks>
public interface IReceiptNumberAllocator
{
    Task<ReceiptNumber> NextAsync(
        ShopId shopId,
        TerminalId terminalId,
        CancellationToken cancellationToken = default);
}
