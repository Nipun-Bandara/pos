using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Tenancy;

/// <summary>
/// The tenant boundary. TEN-01.
/// </summary>
/// <remarks>
/// Every other table in the system carries a ShopId, and PostgreSQL row level security
/// keys off it (TEN-02). One shop is one tenant even when an owner has several branches;
/// the owner's cross-branch view (TEN-06) is built by granting access to several shops,
/// not by weakening the boundary.
/// </remarks>
public sealed class Shop : AggregateRoot<ShopId>
{
    private Shop(ShopId id, string name, string legalName, Currency currency)
        : base(id)
    {
        Name = name;
        LegalName = legalName;
        Currency = currency;
    }

    private Shop()
    {
        Name = null!;
        LegalName = null!;
    }

    /// <summary>Trading name, printed at the top of the receipt.</summary>
    public string Name { get; private set; }

    public string LegalName { get; private set; }

    /// <summary>CMP-05: printed on every receipt where the shop is registered.</summary>
    public string? TaxRegistrationNumber { get; private set; }

    public string? AddressLine1 { get; private set; }

    public string? AddressLine2 { get; private set; }

    public string? Telephone { get; private set; }

    /// <summary>
    /// The shop trades in exactly one currency. Multi-currency is not in scope for v1 and
    /// pinning it here keeps Money comparisons meaningful across the whole tenant.
    /// </summary>
    public Currency Currency { get; private set; }

    /// <summary>IANA identifier, for example "Asia/Colombo". Shift and report boundaries use it.</summary>
    public string TimeZoneId { get; private set; } = "UTC";

    public string? ReceiptFooter { get; private set; }

    public string? PrinterName { get; private set; }

    public int PaperWidthMillimeters { get; private set; } = 80;

    public long CashIncrementMinor { get; private set; } = 100;

    public void UpdateProfile(string name, string legalName, string timeZoneId,
        string? receiptFooter, string? printerName, int paperWidthMillimeters, long cashIncrementMinor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (name.Length > 200 || legalName.Length > 200 || timeZoneId.Length > 100
            || receiptFooter?.Length > 1000 || printerName?.Length > 200
            || paperWidthMillimeters is < 40 or > 300 || cashIncrementMinor is < 1 or > 100_000)
        {
            throw new DomainException("Invalid shop profile.");
        }

        Name = name.Trim();
        LegalName = legalName.Trim();
        TimeZoneId = timeZoneId;
        ReceiptFooter = receiptFooter?.Trim();
        PrinterName = printerName?.Trim();
        PaperWidthMillimeters = paperWidthMillimeters;
        CashIncrementMinor = cashIncrementMinor;
    }

    public bool IsActive { get; private set; } = true;

    public static Shop Create(
        string name,
        string legalName,
        Currency currency,
        string timeZoneId = "UTC",
        ShopId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (!currency.IsSpecified)
        {
            throw new DomainException($"Shop '{name}' must trade in a known currency.");
        }

        return new Shop(id ?? ShopId.New(), name.Trim(), legalName.Trim(), currency)
        {
            TimeZoneId = timeZoneId,
        };
    }

    public void SetTaxRegistration(string? registrationNumber) =>
        TaxRegistrationNumber = string.IsNullOrWhiteSpace(registrationNumber)
            ? null
            : registrationNumber.Trim();

    public void SetContactDetails(string? addressLine1, string? addressLine2, string? telephone)
    {
        AddressLine1 = addressLine1?.Trim();
        AddressLine2 = addressLine2?.Trim();
        Telephone = telephone?.Trim();
    }

    public void Deactivate() => IsActive = false;
}
