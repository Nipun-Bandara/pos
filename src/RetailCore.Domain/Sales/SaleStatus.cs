namespace RetailCore.Domain.Sales;

/// <summary>
/// Where a sale is in its life. Movement between these is one way except for hold and recall.
/// </summary>
public enum SaleStatus
{
    /// <summary>Being rung up. The only state in which lines and payments may change.</summary>
    Open = 0,

    /// <summary>SAL-11: parked so the next customer can be served.</summary>
    Held = 1,

    /// <summary>
    /// Paid and finished. CMP-04 makes this immutable: correcting a completed sale means
    /// voiding it or refunding against it, never editing it.
    /// </summary>
    Completed = 2,

    /// <summary>Abandoned before payment. No financial effect.</summary>
    Cancelled = 3,

    /// <summary>RET-04: reversed after completion, with a reason and an authoriser.</summary>
    Voided = 4,
}
