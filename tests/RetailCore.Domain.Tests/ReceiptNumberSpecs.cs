using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;

namespace RetailCore.Domain.Tests;

public class ReceiptNumberSpecs
{
    [Fact]
    public void Numbering_is_per_terminal_so_offline_tills_do_not_collide()
    {
        // SYN-03. Two tills in one shop, both offline, both issuing receipt 42 without a
        // shared counter to fight over.
        var first = ReceiptNumber.Create("T1", 42);
        var second = ReceiptNumber.Create("T2", 42);

        first.ShouldNotBe(second);
        first.ToString().ShouldBe("T1-000042");
        second.ToString().ShouldBe("T2-000042");
    }

    [Fact]
    public void Numbering_starts_at_one()
    {
        Should.Throw<DomainException>(() => ReceiptNumber.Create("T1", 0));
        Should.Throw<DomainException>(() => ReceiptNumber.Create("T1", -1));
    }

    [Fact]
    public void The_printed_form_pads_so_receipts_sort_as_text()
    {
        ReceiptNumber.Create("T1", 7).ToString().ShouldBe("T1-000007");
        ReceiptNumber.Create("T1", 1234567).ToString().ShouldBe("T1-1234567");
    }

    [Fact]
    public void A_printed_number_can_be_read_back()
    {
        // Needed for HW-05 and BO-04, where a customer hands over a paper receipt.
        var parsed = ReceiptNumber.Parse("T2-000123");

        parsed.TerminalCode.ShouldBe("T2");
        parsed.Sequence.ShouldBe(123);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("T1-")]
    [InlineData("-42")]
    [InlineData("T1-abc")]
    public void Something_that_is_not_a_receipt_number_is_refused(string value)
    {
        Should.Throw<DomainException>(() => ReceiptNumber.Parse(value));
    }

    [Fact]
    public void Numbers_order_within_a_terminal()
    {
        (ReceiptNumber.Create("T1", 1) < ReceiptNumber.Create("T1", 2)).ShouldBeTrue();
        (ReceiptNumber.Create("T1", 10) > ReceiptNumber.Create("T1", 9)).ShouldBeTrue();
    }
}
