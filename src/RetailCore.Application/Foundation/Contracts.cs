using RetailCore.Domain.Common;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Application.Foundation;

public sealed class FoundationException(string key) : Exception(key)
{
    public string Key { get; } = key;
}

public interface IPinHasher
{
    string Hash(string pin);
    bool Verify(string pin, string encodedHash);
}

/// <summary>A fresh unit of work per read or write; entities never escape a transaction.</summary>
public interface ILocalStore
{
    Task<IReadOnlyList<Shop>> ShopsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Terminal>> TerminalsAsync(ShopId shopId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> UsersAsync(ShopId shopId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RetailCore.Domain.Catalogue.Product>> ProductsAsync(ShopId shop, CancellationToken ct);
    Task<IReadOnlyList<RetailCore.Domain.Catalogue.Category>> CategoriesAsync(ShopId shop, CancellationToken ct);
    Task<IReadOnlyDictionary<ProductId, long>> StockAsync(ShopId shop, CancellationToken ct);
    Task<IReadOnlyList<RetailCore.Domain.Tax.TaxRate>> TaxRatesAsync(ShopId shop, CancellationToken ct);
    void Add(RetailCore.Domain.Catalogue.Product product);
    void Add(RetailCore.Domain.Catalogue.Category category);
    void Add(RetailCore.Domain.Tax.TaxRate rate);
    Task<IReadOnlyList<RetailCore.Domain.Inventory.StockMovement>> MovementsAsync(ShopId shop, CancellationToken ct);
    Task PostStockAsync(ShopId shop, string sourceType, Guid sourceId, IReadOnlyList<RetailCore.Domain.Inventory.StockMovement> movements, CancellationToken ct);
    Task<CompletedLocalCommand?> CommandAsync(Guid id, CancellationToken ct);
    void CompleteCommand(Guid id, string kind, string fingerprint, string result);
    void Add(Shop shop);
    void Add(Terminal terminal);
    void Add(User user);
    void Audit(AuditEntry entry);
}

public interface ILocalTransactions
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<T> ReadAsync<T>(Func<ILocalStore, CancellationToken, Task<T>> query,
        CancellationToken cancellationToken = default);
    Task<T> WriteAsync<T>(Func<ILocalStore, CancellationToken, Task<T>> command,
        CancellationToken cancellationToken = default);
    /// <summary>Persist result and command identity together; callbacks may be rerun on SQLite busy.</summary>
    Task<T> ExecuteOnceAsync<T>(Guid operationId, string kind, string fingerprint,
        Func<ILocalStore, CancellationToken, Task<T>> command, CancellationToken cancellationToken = default);
}

public sealed record CompletedLocalCommand(string Kind, string Fingerprint, string Result);

public sealed record UserSummary(UserId Id, string Name, Role Role, bool IsActive, string Language);
public sealed record ShopProfile(string Name, string LegalName, string Currency, string TimeZoneId,
    string? Address, string? Telephone, string? ReceiptFooter, string? PrinterName,
    int PaperWidthMillimeters, long CashIncrementMinor);
public sealed record StartupInfo(bool NeedsSetup, IReadOnlyList<UserSummary> Users);
public sealed record ApprovalRequest(string Action, string Details);
public sealed record ApprovalTicket(Guid Token);
