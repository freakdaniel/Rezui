using Avalonia.Controls;

namespace Rezui.Views;

/// <summary>
/// Startup / authentication overlay: the loading splash, the login wizard
/// page and the mirror-selection wizard page. Hosted above the shell in the
/// window; named elements (LoginEmailInput, LoginPasswordInput, etc.) are
/// preserved so external focus/automation code can still reach them.
/// </summary>
public partial class StartupView : UserControl
{
    public StartupView() => InitializeComponent();
}
