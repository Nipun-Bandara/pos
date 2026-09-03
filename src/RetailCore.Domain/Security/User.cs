using RetailCore.Domain.Common;

namespace RetailCore.Domain.Security;

/// <summary>
/// Somebody who can sign in to a till or the back office. SEC-01.
/// </summary>
/// <remarks>
/// The PIN is stored only as an Argon2id hash (SEC-02). The hashing itself is an
/// infrastructure concern and arrives in Phase 2; the domain holds the hash, the failure
/// counter and the lockout rule (SEC-03) so that the lockout cannot be bypassed by calling
/// a different code path.
/// </remarks>
public sealed class User : AggregateRoot<UserId>, IBelongsToShop
{
    /// <summary>SEC-03. Five attempts is enough for a fat-fingered cashier and few enough to matter.</summary>
    public const int MaxFailedPinAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User(UserId id, ShopId shopId, string displayName, Role role, string pinHash)
        : base(id)
    {
        ShopId = shopId;
        DisplayName = displayName;
        Role = role;
        PinHash = pinHash;
    }

    private User()
    {
        DisplayName = null!;
        PinHash = null!;
    }

    public ShopId ShopId { get; private set; }

    /// <summary>Shown on the till and printed on the receipt.</summary>
    public string DisplayName { get; private set; }

    public Role Role { get; private set; }

    /// <summary>Argon2id hash of the PIN, including its salt and parameters. Never the PIN.</summary>
    public string PinHash { get; private set; }

    public int FailedPinAttempts { get; private set; }

    /// <summary>Set when the account is locked. Null once the lock has been cleared.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static User Create(ShopId shopId, string displayName, Role role, string pinHash, UserId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pinHash);

        return new User(id ?? UserId.New(), shopId, displayName.Trim(), role, pinHash);
    }

    public bool IsLockedAt(DateTimeOffset at) => LockedUntil is { } until && at < until;

    /// <summary>Records a failed PIN entry, locking the account once the limit is reached.</summary>
    public void RecordFailedPinAttempt(DateTimeOffset at)
    {
        FailedPinAttempts++;

        if (FailedPinAttempts >= MaxFailedPinAttempts)
        {
            LockedUntil = at + LockoutDuration;
        }
    }

    /// <summary>Clears the failure counter after a successful sign in.</summary>
    public void RecordSuccessfulSignIn()
    {
        FailedPinAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>Manager or owner action to release a locked account before the timer expires.</summary>
    public void ClearLockout()
    {
        FailedPinAttempts = 0;
        LockedUntil = null;
    }

    public void ChangePin(string pinHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pinHash);
        PinHash = pinHash;
        ClearLockout();
    }

    public void ChangeRole(Role role) => Role = role;

    /// <summary>True when the user may take the action unaided. SEC-05.</summary>
    public bool Can(PrivilegedAction permission) => IsActive && Role.Allows(permission);

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
