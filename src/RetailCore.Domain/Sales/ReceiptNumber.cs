using System.Globalization;
using RetailCore.Domain.Common;

namespace RetailCore.Domain.Sales;

/// <summary>
/// A per-terminal, gapless receipt number. SYN-03.
/// </summary>
/// <remarks>
/// The sequence is per terminal rather than per shop, because a shop-wide sequence would
/// need coordination between tills and the tills are frequently offline. Prefixing with the
/// terminal code keeps the printed number unique across the shop without that coordination.
/// <para>
/// Gapless matters for tax audits: a missing number has to be explainable. The counter is
/// therefore allocated inside the same local transaction that writes the sale, so a crash
/// between allocating and saving cannot burn a number.
/// </para>
/// </remarks>
public readonly record struct ReceiptNumber : IComparable<ReceiptNumber>
{
    private ReceiptNumber(string terminalCode, long sequence)
    {
        TerminalCode = terminalCode;
        Sequence = sequence;
    }

    public string TerminalCode { get; }

    /// <summary>Starts at 1 for each terminal and never skips.</summary>
    public long Sequence { get; }

    public static ReceiptNumber Create(string terminalCode, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalCode);

        if (sequence < 1)
        {
            throw new DomainException($"Receipt sequence {sequence} is not valid; numbering starts at 1.");
        }

        return new ReceiptNumber(terminalCode.Trim().ToUpperInvariant(), sequence);
    }

    /// <summary>Parses the printed form, for example "T1-000042".</summary>
    public static ReceiptNumber Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var separator = value.LastIndexOf('-');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new DomainException($"'{value}' is not a receipt number. Expected a form like T1-000042.");
        }

        var sequenceText = value[(separator + 1)..];
        if (!long.TryParse(sequenceText, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
        {
            throw new DomainException($"'{value}' is not a receipt number. Expected a form like T1-000042.");
        }

        return Create(value[..separator], sequence);
    }

    public int CompareTo(ReceiptNumber other)
    {
        var byTerminal = string.CompareOrdinal(TerminalCode, other.TerminalCode);
        return byTerminal != 0 ? byTerminal : Sequence.CompareTo(other.Sequence);
    }

    public static bool operator <(ReceiptNumber left, ReceiptNumber right) => left.CompareTo(right) < 0;

    public static bool operator >(ReceiptNumber left, ReceiptNumber right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReceiptNumber left, ReceiptNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReceiptNumber left, ReceiptNumber right) => left.CompareTo(right) >= 0;

    /// <summary>The printed form. Padded so receipts sort correctly as text.</summary>
    public override string ToString() =>
        $"{TerminalCode}-{Sequence.ToString("D6", CultureInfo.InvariantCulture)}";
}
