namespace RetailCore.Domain.Sales;

public static class ExternalCardReference
{
    private static readonly string[] CredentialLabels = ["CVV", "CVC", "PIN", "PAN"];
    /// <summary>Only an optional short external approval identifier. Numeric card/PIN/security data is not accepted.</summary>
    public static bool IsValid(string? reference) => string.IsNullOrEmpty(reference)
        || (reference.Length <= 32 && reference.Any(char.IsAsciiLetter) && reference.Count(char.IsAsciiDigit) <= 8
            && reference.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            && !CredentialLabels.Any(s => reference.Contains(s, StringComparison.OrdinalIgnoreCase)));
}
