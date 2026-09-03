using RetailCore.Domain.Security;

namespace RetailCore.Domain.Tests;

public class SecuritySpecs
{
    private readonly TestShop _shop = new();

    [Fact]
    public void An_account_locks_after_repeated_failed_pin_entry()
    {
        // SEC-03.
        var user = Cashier();

        for (var attempt = 0; attempt < User.MaxFailedPinAttempts; attempt++)
        {
            user.IsLockedAt(TestShop.Now).ShouldBeFalse();
            user.RecordFailedPinAttempt(TestShop.Now);
        }

        user.IsLockedAt(TestShop.Now).ShouldBeTrue();
    }

    [Fact]
    public void The_lock_expires_on_its_own()
    {
        var user = Cashier();
        for (var attempt = 0; attempt < User.MaxFailedPinAttempts; attempt++)
        {
            user.RecordFailedPinAttempt(TestShop.Now);
        }

        user.IsLockedAt(TestShop.Now + User.LockoutDuration).ShouldBeFalse();
    }

    [Fact]
    public void A_successful_sign_in_clears_the_counter()
    {
        var user = Cashier();
        user.RecordFailedPinAttempt(TestShop.Now);
        user.RecordFailedPinAttempt(TestShop.Now);

        user.RecordSuccessfulSignIn();

        user.FailedPinAttempts.ShouldBe(0);
    }

    [Fact]
    public void A_manager_can_release_a_locked_account()
    {
        var user = Cashier();
        for (var attempt = 0; attempt < User.MaxFailedPinAttempts; attempt++)
        {
            user.RecordFailedPinAttempt(TestShop.Now);
        }

        user.ClearLockout();

        user.IsLockedAt(TestShop.Now).ShouldBeFalse();
    }

    [Fact]
    public void Only_the_pin_hash_is_ever_held()
    {
        // SEC-02. There is nowhere on the user to put a PIN in the clear.
        var names = typeof(User).GetProperties().Select(p => p.Name).ToList();

        names.ShouldNotContain("Pin");
        names.ShouldNotContain("PlainPin");
        names.ShouldNotContain("Password");
        names.ShouldContain("PinHash");
    }

    [Theory]
    [InlineData(Role.Cashier, PrivilegedAction.ApproveDiscount, false)]
    [InlineData(Role.Cashier, PrivilegedAction.VoidSale, false)]
    [InlineData(Role.Manager, PrivilegedAction.ApproveDiscount, true)]
    [InlineData(Role.Manager, PrivilegedAction.ApproveRefund, true)]
    [InlineData(Role.Manager, PrivilegedAction.ManageUsers, false)]
    [InlineData(Role.Owner, PrivilegedAction.ManageUsers, true)]
    [InlineData(Role.Owner, PrivilegedAction.VoidSale, true)]
    public void Roles_carry_the_authority_the_shop_expects(
        Role role,
        PrivilegedAction action,
        bool allowed)
    {
        // SEC-01 and SEC-05.
        var user = User.Create(_shop.ShopId, "Someone", role, "argon2id$hash");

        user.Can(action).ShouldBe(allowed);
    }

    [Fact]
    public void A_deactivated_user_can_do_nothing()
    {
        var owner = User.Create(_shop.ShopId, "Owner", Role.Owner, "argon2id$hash");
        owner.Deactivate();

        owner.Can(PrivilegedAction.VoidSale).ShouldBeFalse();
    }

    private User Cashier() => User.Create(_shop.ShopId, "Nimal", Role.Cashier, "argon2id$hash");
}
