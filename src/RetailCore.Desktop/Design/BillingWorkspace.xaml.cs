using System.Windows.Controls;
namespace RetailCore.Desktop;

/// <summary>Bounded checkout layout; financial and scan commands remain in the existing presenter.</summary>
public sealed partial class BillingWorkspace : UserControl
{
    public BillingWorkspace()
    {
        InitializeComponent();
        // Expanded product selection scrolls within its own area, preserving basket/edit/payment space.
        Actions.SizeChanged += (_, _) => SearchViewport.MaxHeight = Math.Clamp(Actions.ActualHeight * .32, 90, 240);
    }
}
