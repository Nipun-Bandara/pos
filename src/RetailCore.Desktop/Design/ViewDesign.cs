using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private static readonly HashSet<string> PrimaryActions = ["SignIn", "Unlock", "CreateShop", "CompleteSale", "SaveProduct", "SaveCategory", "PostStockEntry", "CompleteReturn", "SaveSettings", "LoadReport"];

    private static ScrollViewer AuthPage(StackPanel panel)
    {
        panel.MaxWidth = 520;
        var surface = new Border { Child = panel, MaxWidth = 568, Margin = new Thickness(8, 20, 8, 20), HorizontalAlignment = HorizontalAlignment.Center };
        surface.SetResourceReference(StyleProperty, "PosSurfaceBorder");
        return Scroll(surface);
    }

    // Use the same bounded field layout in catalogue, stock, settings and reports.
    private static void ArrangeFields(Panel panel)
    {
        foreach (var child in panel.Children.OfType<Panel>().ToArray()) ArrangeFields(child);
        for (var i = 0; i + 1 < panel.Children.Count; i++)
        {
            if (panel.Children[i] is not TextBlock label || panel.Children[i + 1] is not Control input
                || input is not (TextBox or PasswordBox or ComboBox or DatePicker)) continue;
            if (string.IsNullOrEmpty(AutomationProperties.GetName(input))) AutomationProperties.SetName(input, label.Text);
            var field = new StackPanel { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
            panel.Children.RemoveAt(i); panel.Children.RemoveAt(i);
            field.Children.Add(label); field.Children.Add(input); panel.Children.Insert(i, field);
        }
        // Adjacent fields form responsive rows; short actions share a wrapping toolbar.
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var isField = panel.Children[i] is StackPanel field && field.Width == 260;
            var isButton = panel.Children[i] is Button;
            if (!isField && !isButton) continue;
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            while (i < panel.Children.Count && (isField
                ? panel.Children[i] is StackPanel next && next.Width == 260
                : panel.Children[i] is Button))
            {
                var item = panel.Children[i]; panel.Children.RemoveAt(i); row.Children.Add(item);
            }
            panel.Children.Insert(i, row);
        }
    }

    private static Grid WithEmptyState(DataGrid table)
    {
        var grid = new Grid(); grid.Children.Add(table);
        var empty = new TextBlock { Text = Texts.Get("NoRows"), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Margin = new Thickness(16) };
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var trigger = new DataTrigger { Binding = new Binding(nameof(ItemsControl.HasItems)) { Source = table }, Value = false };
        trigger.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible)); style.Triggers.Add(trigger); empty.Style = style;
        AutomationProperties.SetLiveSetting(empty, AutomationLiveSetting.Polite);
        grid.Children.Add(empty); return grid;
    }
}
