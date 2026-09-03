namespace RetailCore.Domain.Common;

/// <summary>
/// Raised when an operation would break a domain invariant.
/// </summary>
/// <remarks>
/// FEATURES.md "Definition of done" item 4: errors carry a message a cashier can act on.
/// Keep these messages in plain language, not developer shorthand, because they are
/// surfaced on the till.
/// </remarks>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException()
    {
    }
}
