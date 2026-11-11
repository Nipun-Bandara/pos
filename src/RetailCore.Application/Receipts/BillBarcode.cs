using RetailCore.Application.Foundation;

namespace RetailCore.Application.Receipts;

/// <summary>Code 39, ratio 3:1, inter-character narrow space and ten-module quiet zones.</summary>
public static class BillBarcode
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";
    private static readonly int[] Patterns = [0x034,0x121,0x061,0x160,0x031,0x130,0x070,0x025,0x124,0x064,
        0x109,0x049,0x148,0x019,0x118,0x058,0x00D,0x10C,0x04C,0x01C,0x103,0x043,0x142,0x013,0x112,
        0x052,0x007,0x106,0x046,0x016,0x181,0x0C1,0x1C0,0x091,0x190,0x0D0,0x085,0x184,0x0C4,0x0A8,0x0A2,0x08A,0x02A];
    public static IReadOnlyList<bool> Encode(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40 || value.Any(c => !Alphabet.Contains(c)))
            throw new FoundationException("BillBarcodeUnavailable");
        var modules = new List<bool>(800); modules.AddRange(Enumerable.Repeat(false, 10));
        var symbols = "*" + value + "*";
        for (var s = 0; s < symbols.Length; s++)
        {
            var pattern = symbols[s] == '*' ? 0x094 : Patterns[Alphabet.IndexOf(symbols[s])];
            for (var i = 0; i < 9; i++) modules.AddRange(Enumerable.Repeat(i % 2 == 0, (pattern & (1 << (8-i))) != 0 ? 3 : 1));
            if (s < symbols.Length - 1) modules.Add(false);
        }
        modules.AddRange(Enumerable.Repeat(false, 10)); return modules;
    }
}
