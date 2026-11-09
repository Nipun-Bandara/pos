using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Application.Foundation;

/// <summary>Single-shop setup, authentication and owner commands. All writes include audit.</summary>
public sealed class FoundationService(ILocalTransactions transactions, IPinHasher hasher,
    LocalSession session, IClock clock)
{
    private sealed record Authentication(UserSummary User, string CredentialHash);
    private sealed record Grant(UserId Requester, UserId Owner, string OwnerHash,
        Guid SessionVersion, ApprovalRequest Request, DateTimeOffset ExpiresAt);
    private readonly Dictionary<Guid, Grant> _grants = [];

    public async Task<StartupInfo> StartAsync(CancellationToken cancellationToken = default)
    {
        await transactions.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await transactions.ReadAsync(async (store, ct) =>
        {
            var shops = await store.ShopsAsync(ct).ConfigureAwait(false);
            if (shops.Count == 0)
            {
                return new StartupInfo(true, []);
            }

            var shop = SingleShop(shops);
            var terminals = await store.TerminalsAsync(shop.Id, ct).ConfigureAwait(false);
            if (terminals.Count != 1 || !terminals[0].IsActive)
            {
                throw new FoundationException("LocalConfiguration");
            }

            var users = await store.UsersAsync(shop.Id, ct).ConfigureAwait(false);
            if (!users.Any(u => u.IsActive && u.Role == Role.Owner))
            {
                throw new FoundationException("OwnerMissing");
            }

            return new StartupInfo(false, users.Where(u => u.IsActive && u.Role is Role.Owner or Role.Cashier)
                .Select(Summary).ToArray());
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetupAsync(ShopProfile profile, string ownerName, string pin, string language,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        ValidateUser(ownerName, pin, language, Role.Owner);
        var hash = hasher.Hash(pin);
        await transactions.WriteAsync(async (store, ct) =>
        {
            if ((await store.ShopsAsync(ct).ConfigureAwait(false)).Count != 0)
            {
                throw new FoundationException("AlreadyConfigured");
            }

            var shop = Shop.Create(profile.Name, profile.LegalName, Currency.FromCode(profile.Currency), profile.TimeZoneId);
            ApplyProfile(shop, profile);
            var terminal = Terminal.Create(shop.Id, "T1", "T1");
            var owner = User.Create(shop.Id, ownerName.Trim(), Role.Owner, hash);
            owner.SetLanguage(language);
            store.Add(shop);
            store.Add(terminal);
            store.Add(owner);
            Record(store, shop.Id, AuditAction.ShopConfigured, owner.Id, "Shop", shop.Id.Value);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserSummary> SignInAsync(UserId userId, string pin, bool unlocking = false,
        CancellationToken cancellationToken = default)
    {
        var previous = session.UserId;
        if (unlocking && (previous != userId || !session.IsLocked))
        {
            throw new FoundationException("SignInRequired");
        }

        var authenticated = await AuthenticateAsync(userId, pin, cancellationToken).ConfigureAwait(false);
        var user = authenticated.User;
        session.SignIn(user.Id, authenticated.CredentialHash);
        _grants.Clear();
        return user;
    }

    public void Lock()
    {
        session.Lock();
        _grants.Clear();
    }

    public void SignOut()
    {
        session.SignOut();
        _grants.Clear();
    }

    private async Task<Authentication> AuthenticateAsync(UserId userId, string pin, CancellationToken cancellationToken)
    {
        // Return failure *after* commit so counters/lockout are never rolled back by authentication failure.
        var result = await transactions.WriteAsync(async (store, ct) =>
        {
            var shop = SingleShop(await store.ShopsAsync(ct).ConfigureAwait(false));
            var users = await store.UsersAsync(shop.Id, ct).ConfigureAwait(false);
            var user = users.SingleOrDefault(u => u.Id == userId);
            if (user is null || !user.IsActive || user.Role is not (Role.Owner or Role.Cashier))
            {
                return (User: (Authentication?)null, Error: "InvalidCredentials");
            }

            if (user.IsLockedAt(clock.UtcNow))
            {
                return (User: (Authentication?)null, Error: "AccountLocked");
            }

            if (!hasher.Verify(pin, user.PinHash))
            {
                if (user.LockedUntil is not null) { user.ClearLockout(); }
                user.RecordFailedPinAttempt(clock.UtcNow);
                if (user.IsLockedAt(clock.UtcNow))
                {
                    Record(store, shop.Id, AuditAction.UserLockedOut, user.Id, "User", user.Id.Value);
                }

                return (User: (Authentication?)null,
                    Error: user.IsLockedAt(clock.UtcNow) ? "AccountLocked" : "InvalidCredentials");
            }

            user.RecordSuccessfulSignIn();
            Record(store, shop.Id, AuditAction.UserSignedIn, user.Id, "User", user.Id.Value);
            return (User: (Authentication?)new Authentication(Summary(user), user.PinHash), Error: "");
        }, cancellationToken).ConfigureAwait(false);
        return result.User ?? throw new FoundationException(result.Error);
    }

    public Task<UserSummary> CurrentAsync(CancellationToken cancellationToken = default) =>
        transactions.ReadAsync(async (store, ct) => Summary(await RequireUserAsync(store, false, ct).ConfigureAwait(false)), cancellationToken);

    public Task<ShopProfile> ProfileAsync(CancellationToken cancellationToken = default) =>
        transactions.ReadAsync(async (store, ct) =>
        {
            await RequireUserAsync(store, true, ct).ConfigureAwait(false);
            var shop = SingleShop(await store.ShopsAsync(ct).ConfigureAwait(false));
            return new ShopProfile(shop.Name, shop.LegalName, shop.Currency.Code, shop.TimeZoneId,
                shop.AddressLine1, shop.Telephone, shop.ReceiptFooter, shop.PrinterName,
                shop.PaperWidthMillimeters, shop.CashIncrementMinor);
        }, cancellationToken);

    public Task<IReadOnlyList<UserSummary>> UsersAsync(CancellationToken cancellationToken = default) =>
        transactions.ReadAsync<IReadOnlyList<UserSummary>>(async (store, ct) =>
        {
            var actor = await RequireUserAsync(store, true, ct).ConfigureAwait(false);
            return (await store.UsersAsync(actor.ShopId, ct).ConfigureAwait(false)).Select(Summary).ToArray();
        }, cancellationToken);

    public async Task SetLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        if (language is not ("en" or "si")) { throw new FoundationException("InvalidLanguage"); }
        await transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await RequireUserAsync(store, false, ct).ConfigureAwait(false);
            actor.SetLanguage(language);
            Record(store, actor.ShopId, AuditAction.LanguageChanged, actor.Id, "User", actor.Id.Value);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opaque approval bound to exact action/details, requester/session and owner credential version.</summary>
    public async Task<ApprovalTicket> ApproveAsync(UserId ownerId, string pin, ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Action) || request.Action.Length > 100 || request.Details.Length > 4000)
        {
            throw new FoundationException("ApprovalRequired");
        }

        var requester = await CurrentAsync(cancellationToken).ConfigureAwait(false);
        var version = session.Version;
        var authenticated = await AuthenticateAsync(ownerId, pin, cancellationToken).ConfigureAwait(false);
        var owner = authenticated.User;
        if (owner.Role != Role.Owner) { throw new FoundationException("OwnerRequired"); }
        var ownerHash = authenticated.CredentialHash;
        if (version != session.Version) { throw new FoundationException("ApprovalRequired"); }
        var token = Guid.NewGuid();
        foreach (var key in _grants.Where(g => g.Value.ExpiresAt <= clock.UtcNow).Select(g => g.Key).ToArray())
        {
            _grants.Remove(key);
        }

        _grants[token] = new Grant(requester.Id, ownerId, ownerHash, version, request, clock.UtcNow.AddMinutes(2));
        return new ApprovalTicket(token);
    }

    public static ApprovalRequest SettingsApproval(ShopProfile profile) =>
        new("ShopSettings", System.Text.Json.JsonSerializer.Serialize(profile));

    public async Task SaveProfileAsync(ShopProfile profile, ApprovalTicket ticket, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        var grant = TakeGrant(ticket, SettingsApproval(profile));
        await transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await RequireUserAsync(store, true, ct).ConfigureAwait(false);
            await CheckGrantOwnerAsync(store, actor, grant, ct).ConfigureAwait(false);
            var shop = SingleShop(await store.ShopsAsync(ct).ConfigureAwait(false));
            if (shop.Currency.Code != profile.Currency) { throw new FoundationException("CurrencyFixed"); }
            var before = new { shop.Name, shop.LegalName, shop.TimeZoneId, shop.AddressLine1, shop.Telephone,
                shop.ReceiptFooter, shop.PrinterName, shop.PaperWidthMillimeters, shop.CashIncrementMinor };
            ApplyProfile(shop, profile);
            Record(store, actor.ShopId, AuditAction.ShopSettingsChanged, actor.Id, "Shop", shop.Id.Value, grant.Owner,
                System.Text.Json.JsonSerializer.Serialize(new { Before = before, After = profile }));
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateUserAsync(string name, string pin, Role role, string language,
        CancellationToken cancellationToken = default)
    {
        ValidateUser(name, pin, language, role);
        var hash = hasher.Hash(pin);
        await transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await RequireUserAsync(store, true, ct).ConfigureAwait(false);
            var users = await store.UsersAsync(actor.ShopId, ct).ConfigureAwait(false);
            if (users.Any(u => string.Equals(u.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new FoundationException("DuplicateUser");
            }

            var user = User.Create(actor.ShopId, name.Trim(), role, hash);
            user.SetLanguage(language);
            store.Add(user);
            Record(store, actor.ShopId, AuditAction.UserChanged, actor.Id, "User", user.Id.Value);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateUserAsync(UserId id, Role role, bool active, string? newPin, bool clearLockout,
        CancellationToken cancellationToken = default)
    {
        if (role is not (Role.Owner or Role.Cashier)) { throw new FoundationException("InvalidUser"); }
        if (!string.IsNullOrEmpty(newPin)) { ValidatePin(newPin); }
        var hash = string.IsNullOrEmpty(newPin) ? null : hasher.Hash(newPin);
        await transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await RequireUserAsync(store, true, ct).ConfigureAwait(false);
            var users = await store.UsersAsync(actor.ShopId, ct).ConfigureAwait(false);
            var user = users.SingleOrDefault(u => u.Id == id) ?? throw new FoundationException("InvalidUser");
            if (user.IsActive && user.Role == Role.Owner && (!active || role != Role.Owner)
                && users.Count(u => u.IsActive && u.Role == Role.Owner) == 1)
            {
                throw new FoundationException("LastOwner");
            }

            var before = new { user.Role, user.IsActive };
            user.ChangeRole(role);
            if (active) { user.Reactivate(); } else { user.Deactivate(); }
            if (hash is not null) { user.ChangePin(hash); }
            if (clearLockout) { user.ClearLockout(); }
            Record(store, actor.ShopId, AuditAction.UserChanged, actor.Id, "User", user.Id.Value,
                details: System.Text.Json.JsonSerializer.Serialize(new { Before = before, Role = role,
                    Active = active, PinChanged = hash is not null, LockoutCleared = clearLockout }));
            return true;
        }, cancellationToken).ConfigureAwait(false);
        _grants.Clear();
        if (session.UserId == id) { session.SignOut(); }
    }

    private Grant TakeGrant(ApprovalTicket ticket, ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!_grants.Remove(ticket.Token, out var grant) || grant.Request != request
            || grant.SessionVersion != session.Version || grant.Requester != session.UserId
            || session.IsLocked || grant.ExpiresAt <= clock.UtcNow)
        {
            throw new FoundationException("ApprovalRequired");
        }

        return grant;
    }

    private async Task CheckGrantOwnerAsync(ILocalStore store, User actor, Grant grant, CancellationToken ct)
    {
        var owner = (await store.UsersAsync(actor.ShopId, ct).ConfigureAwait(false)).SingleOrDefault(u => u.Id == grant.Owner);
        if (owner is null || !owner.IsActive || owner.Role != Role.Owner || owner.PinHash != grant.OwnerHash
            || owner.IsLockedAt(clock.UtcNow) || session.Version != grant.SessionVersion || grant.ExpiresAt <= clock.UtcNow)
        {
            throw new FoundationException("ApprovalRequired");
        }
    }

    internal async Task<User> RequireUserAsync(ILocalStore store, bool ownerOnly, CancellationToken ct)
    {
        if (session.UserId is not { } id || session.IsLocked) { throw new FoundationException("SignInRequired"); }
        var shop = SingleShop(await store.ShopsAsync(ct).ConfigureAwait(false));
        var user = (await store.UsersAsync(shop.Id, ct).ConfigureAwait(false)).SingleOrDefault(u => u.Id == id);
        if (user is null || !user.IsActive || user.PinHash != session.CredentialHash || user.IsLockedAt(clock.UtcNow) || user.Role is not (Role.Owner or Role.Cashier))
        {
            throw new FoundationException("SignInRequired");
        }

        if (ownerOnly && user.Role != Role.Owner) { throw new FoundationException("OwnerRequired"); }
        return user;
    }

    private static Shop SingleShop(IReadOnlyList<Shop> shops) =>
        shops.Count == 1 && shops[0].IsActive ? shops[0] : throw new FoundationException("LocalConfiguration");

    private static UserSummary Summary(User user) => new(user.Id, user.DisplayName, user.Role, user.IsActive, user.Language);

    private void Record(ILocalStore store, ShopId shopId, AuditAction action, UserId actor,
        string entityType, Guid entityId, UserId? approver = null, string? details = null) =>
        store.Audit(AuditEntry.Record(shopId, action, entityType, entityId, clock.UtcNow, actor, approver,
            detailsJson: details));

    private static void ValidatePin(string pin)
    {
        if (pin.Length is < 6 or > 12 || pin.Any(c => c is < '0' or > '9'))
        {
            throw new FoundationException("InvalidPin");
        }
    }

    private static void ValidateUser(string name, string pin, string language, Role role)
    {
        ValidatePin(pin);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || role is not (Role.Owner or Role.Cashier))
        {
            throw new FoundationException("InvalidUser");
        }

        if (language is not ("en" or "si")) { throw new FoundationException("InvalidLanguage"); }
    }

    private static void ValidateProfile(ShopProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200 || string.IsNullOrWhiteSpace(p.LegalName)
            || p.LegalName.Length > 200 || p.Address?.Length > 200 || p.Telephone?.Length > 50
            || p.ReceiptFooter?.Length > 1000 || p.PrinterName?.Length > 200
            || p.PaperWidthMillimeters is < 40 or > 300 || p.CashIncrementMinor is < 1 or > 100_000
            || !Currency.TryFromCode(p.Currency, out _) || string.IsNullOrWhiteSpace(p.TimeZoneId))
        {
            throw new FoundationException("InvalidProfile");
        }

        try { TimeZoneInfo.FindSystemTimeZoneById(p.TimeZoneId); }
        catch (TimeZoneNotFoundException) { throw new FoundationException("InvalidTimeZone"); }
        catch (InvalidTimeZoneException) { throw new FoundationException("InvalidTimeZone"); }
    }

    private static void ApplyProfile(Shop shop, ShopProfile profile)
    {
        shop.UpdateProfile(profile.Name, profile.LegalName, profile.TimeZoneId, profile.ReceiptFooter,
            profile.PrinterName, profile.PaperWidthMillimeters, profile.CashIncrementMinor);
        shop.SetContactDetails(profile.Address, shop.AddressLine2, profile.Telephone);
    }
}
