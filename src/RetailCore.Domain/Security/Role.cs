namespace RetailCore.Domain.Security;

/// <summary>
/// SEC-01. Three roles, deliberately.
/// </summary>
/// <remarks>
/// FEATURES.md puts granular permissions at SEC-09, priority Could, phase 7. Three roles
/// cover what a minimart actually needs, and a permission matrix built before anyone has
/// asked for one is a configuration screen nobody will fill in correctly.
/// </remarks>
public enum Role
{
    /// <summary>Rings sales. Cannot authorise the things that need a second pair of eyes.</summary>
    Cashier = 0,

    /// <summary>Cashier, plus the overrides in <see cref="PrivilegedAction"/>.</summary>
    Manager = 1,

    /// <summary>Everything, including the back office and other branches.</summary>
    Owner = 2,
}

/// <summary>
/// The privileged actions that need authorising. SEC-05.
/// </summary>
public enum PrivilegedAction
{
    /// <summary>SAL-09: a discount above the shop's threshold.</summary>
    ApproveDiscount = 0,

    /// <summary>RET-05.</summary>
    ApproveRefund = 1,

    /// <summary>RET-05.</summary>
    VoidSale = 2,

    /// <summary>CSH-05 and CSH-06.</summary>
    MoveCash = 3,

    /// <summary>CSH-04: closing the shift and producing the Z report.</summary>
    CloseShift = 4,

    /// <summary>SAL-13: overriding a price at the till.</summary>
    OverridePrice = 5,

    /// <summary>INV-05 and INV-06.</summary>
    AdjustStock = 6,

    /// <summary>BO-02 and BO-03.</summary>
    ManageCatalogue = 7,

    ManageUsers = 8,
}

public static class RolePrivileges
{
    private static readonly PrivilegedAction[] ManagerPrivileges =
    [
        PrivilegedAction.ApproveDiscount,
        PrivilegedAction.ApproveRefund,
        PrivilegedAction.VoidSale,
        PrivilegedAction.MoveCash,
        PrivilegedAction.CloseShift,
        PrivilegedAction.OverridePrice,
        PrivilegedAction.AdjustStock,
    ];

    /// <summary>True when the role may perform the action without a further override.</summary>
    public static bool Allows(this Role role, PrivilegedAction permission) => role switch
    {
        Role.Owner => true,
        Role.Manager => Array.IndexOf(ManagerPrivileges, permission) >= 0,
        Role.Cashier => false,
        _ => false,
    };
}
