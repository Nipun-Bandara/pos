using System.Globalization;
using System.Resources;

namespace RetailCore.Desktop;

public static class Texts
{
    private static readonly ResourceManager Resources = new("RetailCore.Desktop.Resources.Strings", typeof(Texts).Assembly);
    public static string Language { get; private set; } = "en";
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? Resources.GetString("UnexpectedError", CultureInfo.InvariantCulture)!;
    public static void Use(string language)
    {
        Language = language == "si" ? "si" : "en";
        var culture = CultureInfo.GetCultureInfo(Language == "si" ? "si-LK" : "en-LK");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }
}
