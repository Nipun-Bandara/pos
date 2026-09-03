using RetailCore.Domain.Common;

namespace RetailCore.Domain.Catalogue;

/// <summary>
/// A grouping of products, used for the till's category grid and for reporting. CAT-02.
/// </summary>
public sealed class Category : Entity<CategoryId>, IBelongsToShop
{
    private Category(CategoryId id, ShopId shopId, string name, CategoryId? parentId, int displayOrder)
        : base(id)
    {
        ShopId = shopId;
        Name = name;
        ParentId = parentId;
        DisplayOrder = displayOrder;
    }

    private Category() => Name = null!;

    public ShopId ShopId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Null for a top level category.</summary>
    public CategoryId? ParentId { get; private set; }

    /// <summary>Position on the category grid. SAL-03.</summary>
    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Category Create(
        ShopId shopId,
        string name,
        CategoryId? parentId = null,
        int displayOrder = 0,
        CategoryId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Category(id ?? CategoryId.New(), shopId, name.Trim(), parentId, displayOrder);
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void MoveTo(int displayOrder) => DisplayOrder = displayOrder;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
