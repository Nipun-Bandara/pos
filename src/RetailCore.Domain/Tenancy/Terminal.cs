using RetailCore.Domain.Common;

namespace RetailCore.Domain.Tenancy;

/// <summary>
/// One till in one shop. TEN-05.
/// </summary>
/// <remarks>
/// The terminal matters to the domain, not just to operations, because receipt numbering
/// is per terminal (SYN-03) and a shift is per terminal (CSH-02). Two tills in the same
/// shop have to be able to trade offline simultaneously without colliding, and the
/// terminal's own numbering sequence is what makes that possible.
/// </remarks>
public sealed class Terminal : AggregateRoot<TerminalId>, IBelongsToShop
{
    private Terminal(TerminalId id, ShopId shopId, string code, string name)
        : base(id)
    {
        ShopId = shopId;
        Code = code;
        Name = name;
    }

    private Terminal()
    {
        Code = null!;
        Name = null!;
    }

    public ShopId ShopId { get; private set; }

    /// <summary>
    /// Short code that prefixes receipt numbers, for example "T1". Unique within the shop,
    /// and it appears on printed receipts so a support call can identify the till.
    /// </summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>SEC-07: enrolment can be revoked centrally to cut off a lost or stolen till.</summary>
    public bool IsEnrolled { get; private set; }

    public DateTimeOffset? EnrolledAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>OPS-05 and SYN-07: what the back office shows for this till's sync health.</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Reported by the till so support starts from a known build. PLAN.md section 5.</summary>
    public string? InstalledVersion { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Terminal Create(ShopId shopId, string code, string name, TerminalId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Terminal(id ?? TerminalId.New(), shopId, code.Trim().ToUpperInvariant(), name.Trim());
    }

    public void Enrol(DateTimeOffset at)
    {
        IsEnrolled = true;
        EnrolledAt = at;
        RevokedAt = null;
    }

    public void RevokeEnrolment(DateTimeOffset at)
    {
        if (!IsEnrolled)
        {
            throw new DomainException($"Terminal {Code} is not enrolled, so it cannot be revoked.");
        }

        IsEnrolled = false;
        RevokedAt = at;
    }

    public void RecordSync(DateTimeOffset at) => LastSyncedAt = at;

    public void RecordVersion(string version) => InstalledVersion = version;

    public void Deactivate() => IsActive = false;
}
