using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Shifts;

namespace RetailCore.Domain.Tests;

/// <summary>
/// PLAN.md names "the Z report matches the physical cash in the drawer" as the proof that
/// the money model is right end to end. These specifications are that proof in miniature.
/// </summary>
public class ShiftSpecs
{
    private readonly TestShop _shop = new();

    [Fact]
    public void A_shift_opens_with_a_counted_float()
    {
        // CSH-01.
        var shift = OpenShift(5_000.00m);

        shift.OpeningFloat.ShouldBe(_shop.Rupees(5_000.00m));
        shift.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_float_is_refused()
    {
        Should.Throw<DomainException>(() => Shift.Open(
            _shop.ShopId, _shop.TerminalId, _shop.CashierId, _shop.Rupees(-1m), TestShop.Now));
    }

    [Fact]
    public void Expected_cash_is_the_float_plus_cash_taken_less_cash_returned()
    {
        // CSH-04.
        var shift = OpenShift(5_000.00m);
        var summary = Summary(cashSales: 12_000.00m, cashRefunds: 500.00m);

        shift.ExpectedCashFrom(summary).ShouldBe(_shop.Rupees(16_500.00m));
    }

    [Fact]
    public void Card_takings_never_reach_the_drawer_and_are_excluded()
    {
        // The single most common way a Z report becomes unexplainable.
        var shift = OpenShift(5_000.00m);
        var summary = Summary(cashSales: 10_000.00m, nonCashSales: 40_000.00m);

        shift.ExpectedCashFrom(summary).ShouldBe(_shop.Rupees(15_000.00m));
    }

    [Fact]
    public void Pay_ins_and_pay_outs_move_the_expected_figure()
    {
        // CSH-05 and CSH-06.
        var shift = OpenShift(5_000.00m);
        shift.RecordCashMovement(
            CashMovementKind.PayIn, _shop.Rupees(2_000.00m), "Change from the safe", _shop.ManagerId, TestShop.Now);
        shift.RecordCashMovement(
            CashMovementKind.PayOut, _shop.Rupees(750.00m), "Paid the bread supplier", _shop.ManagerId, TestShop.Now);
        shift.RecordCashMovement(
            CashMovementKind.Drop, _shop.Rupees(10_000.00m), "Drawer getting heavy", _shop.ManagerId, TestShop.Now);

        shift.NetCashMovements.ShouldBe(_shop.Rupees(-8_750.00m));
        shift.ExpectedCashFrom(Summary(cashSales: 20_000.00m)).ShouldBe(_shop.Rupees(16_250.00m));
    }

    [Fact]
    public void A_cash_movement_without_a_reason_is_refused()
    {
        var shift = OpenShift(5_000.00m);

        Should.Throw<ArgumentException>(() => shift.RecordCashMovement(
            CashMovementKind.PayOut, _shop.Rupees(100m), "  ", _shop.ManagerId, TestShop.Now));
    }

    [Fact]
    public void Closing_reports_the_variance_between_counted_and_expected()
    {
        // CSH-04. A drawer 250.00 short is the number the owner actually wants.
        var shift = OpenShift(5_000.00m);
        var summary = Summary(cashSales: 12_000.00m);

        var report = shift.Close(summary, _shop.Rupees(16_750.00m), _shop.ManagerId, TestShop.Now);

        report.Kind.ShouldBe(ShiftReportKind.Z);
        report.ExpectedCash.ShouldBe(_shop.Rupees(17_000.00m));
        report.CountedCash.ShouldBe(_shop.Rupees(16_750.00m));
        report.Variance.ShouldBe(_shop.Rupees(-250.00m));
        report.IsShort.ShouldBeTrue();
        report.IsBalanced.ShouldBeFalse();
    }

    [Fact]
    public void A_drawer_that_counts_exactly_balances()
    {
        var shift = OpenShift(5_000.00m);
        var summary = Summary(cashSales: 12_000.00m);

        var report = shift.Close(summary, _shop.Rupees(17_000.00m), _shop.ManagerId, TestShop.Now);

        report.IsBalanced.ShouldBeTrue();
        report.Variance.ShouldBe(Money.Zero(TestShop.Currency));
    }

    [Fact]
    public void The_X_report_does_not_close_the_shift()
    {
        // CSH-03. A manager checking at lunchtime must not end the cashier's shift.
        var shift = OpenShift(5_000.00m);

        var report = shift.ReportX(Summary(cashSales: 3_000.00m), TestShop.Now);

        report.Kind.ShouldBe(ShiftReportKind.X);
        report.CountedCash.ShouldBeNull();
        report.Variance.ShouldBeNull();
        shift.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_closed_shift_cannot_be_closed_again_or_take_more_cash()
    {
        var shift = OpenShift(5_000.00m);
        shift.Close(Summary(), _shop.Rupees(5_000.00m), _shop.ManagerId, TestShop.Now);

        Should.Throw<DomainException>(() =>
            shift.Close(Summary(), _shop.Rupees(5_000.00m), _shop.ManagerId, TestShop.Now));
        Should.Throw<DomainException>(() => shift.RecordCashMovement(
            CashMovementKind.PayIn, _shop.Rupees(10m), "Too late", _shop.ManagerId, TestShop.Now));
    }

    [Fact]
    public void The_report_separates_takings_from_refunds()
    {
        var shift = OpenShift(5_000.00m);
        var summary = Summary(
            cashSales: 10_000.00m,
            cashRefunds: 400.00m,
            nonCashSales: 25_000.00m,
            nonCashRefunds: 600.00m);

        var report = shift.Close(summary, _shop.Rupees(14_600.00m), _shop.ManagerId, TestShop.Now);

        report.GrossTakings.ShouldBe(_shop.Rupees(35_000.00m));
        report.TotalRefunds.ShouldBe(_shop.Rupees(1_000.00m));
        report.NetTakings.ShouldBe(_shop.Rupees(34_000.00m));
        report.IsBalanced.ShouldBeTrue();
    }

    private Shift OpenShift(decimal openingFloat) =>
        Shift.Open(_shop.ShopId, _shop.TerminalId, _shop.CashierId, _shop.Rupees(openingFloat), TestShop.Now);

    private ShiftSalesSummary Summary(
        decimal cashSales = 0m,
        decimal cashRefunds = 0m,
        decimal nonCashSales = 0m,
        decimal nonCashRefunds = 0m) =>
        new(
            _shop.Rupees(cashSales),
            _shop.Rupees(cashRefunds),
            _shop.Rupees(nonCashSales),
            _shop.Rupees(nonCashRefunds),
            SaleCount: 0,
            RefundCount: 0,
            _shop.Rupees(0m),
            _shop.Rupees(0m),
            _shop.Rupees(0m));
}
