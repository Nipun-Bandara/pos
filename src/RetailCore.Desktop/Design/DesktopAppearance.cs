using System.IO;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace RetailCore.Desktop;

/// <summary>Per-Windows-profile appearance only; never changes shop/financial data.</summary>
public sealed class DesktopAppearance(string path)
{
    public bool IsDark { get; private set; } = Read(path);
    private static bool Read(string file)
    {
        try { return File.Exists(file) && File.ReadAllText(file).Trim() == "dark"; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public void Toggle()
    {
        var next = !IsDark;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(next ? "dark" : "light");
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temporary, path, true); IsDark = next; Apply();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Apply()
    {
        var palette = new PaletteHelper(); var theme = palette.GetTheme();
        theme.SetBaseTheme(IsDark ? BaseTheme.Dark : BaseTheme.Light); palette.SetTheme(theme);
        var colors = new Dictionary<string, string>
        {
            ["PosBackground"] = IsDark ? "#111827" : "#F6F7F9",
            ["PosSurface"] = IsDark ? "#1F2937" : "#FFFFFF",
            ["PosInk"] = IsDark ? "#F9FAFB" : "#172033",
            ["PosMuted"] = IsDark ? "#CBD5E1" : "#526077",
            ["PosBorder"] = IsDark ? "#64748B" : "#D8DEE8",
            ["PosAccent"] = IsDark ? "#93C5FD" : "#1D4ED8",
            ["PosAccentSoft"] = IsDark ? "#243F65" : "#EBF1FF",
            ["PosOnAccent"] = IsDark ? "#111827" : "#FFFFFF",
            ["PosAlternate"] = IsDark ? "#253247" : "#FAFBFD",
            ["PosError"] = IsDark ? "#FCA5A5" : "#B42318"
        };
        foreach (var (key, value) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze();
            System.Windows.Application.Current.Resources[key] = brush;
        }
    }
}
