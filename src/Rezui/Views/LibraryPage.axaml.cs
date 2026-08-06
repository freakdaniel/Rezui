using Avalonia.Controls;

namespace Rezui.Views;

/// <summary>
/// Library page: search, continue-watching, results and bookmark folders.
/// Pure markup host — all behaviour lives on MainWindowViewModel, resolved
/// through compiled bindings inherited from the window DataContext.
/// </summary>
public partial class LibraryPage : UserControl
{
    public LibraryPage() => InitializeComponent();
}
