namespace RetailCore.Domain.Tax;

/// <summary>
/// How a product is treated for tax. CMP-03.
/// </summary>
/// <remarks>
/// Zero rated and exempt both collect nothing, and it is tempting to collapse them into a
/// single "no tax" case. They are not the same on a return: zero-rated turnover is
/// reportable and generally allows input tax recovery, exempt turnover does not. Merging
/// them makes the tax summary (BO-09) wrong in a way nobody notices until an audit.
/// </remarks>
public enum TaxCategory
{
    /// <summary>Taxed at the rate carried on the rate record.</summary>
    Standard = 0,

    /// <summary>Taxable supply at a rate of zero. Reportable turnover.</summary>
    ZeroRated = 1,

    /// <summary>Outside the tax entirely. Reported separately from zero-rated.</summary>
    Exempt = 2,
}
