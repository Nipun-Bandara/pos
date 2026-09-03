namespace RetailCore.Domain.Common;

/// <summary>
/// Marks a row as belonging to exactly one shop. TEN-01.
/// </summary>
/// <remarks>
/// <para>
/// Every table in the system carries a shop, including the child tables of a sale. It would
/// be enough for correctness to reach the shop through the parent, but row level security
/// (TEN-02) is a per-table policy, and a table without its own shop column cannot have one.
/// A hand-written query against sale lines would then read every shop's lines.
/// </para>
/// <para>
/// The architecture tests assert that every persisted entity implements this, so a new table
/// cannot be added without deciding which shop it belongs to.
/// </para>
/// </remarks>
public interface IBelongsToShop
{
    ShopId ShopId { get; }
}
