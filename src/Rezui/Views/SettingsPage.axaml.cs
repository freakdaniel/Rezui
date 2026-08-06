using Avalonia.Controls;

namespace Rezui.Views;

/// <summary>
/// Settings page: theme, GPU adapter, diagnostics and the current profile.
/// Pure markup host bound to MainWindowViewModel via compiled bindings.
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}
