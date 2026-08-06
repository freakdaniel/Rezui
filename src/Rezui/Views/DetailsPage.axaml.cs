using Avalonia;
using Avalonia.Controls;

namespace Rezui.Views;

/// <summary>
/// Title details page: hero, skeleton, load-error fallback and the rich
/// body (facts, ratings, cast, schedule, recommendations, comments).
///
/// Owns the reset-to-top behaviour of the details ScrollViewer that used to
/// live on MainWindow. When the page becomes visible the scroll position is
/// forced back to the top so a previously-scrolled view does not retain its
/// offset for the next title.
/// </summary>
public partial class DetailsPage : UserControl
{
    public DetailsPage()
    {
        InitializeComponent();
        DetailsScrollViewer.PropertyChanged += OnDetailsScrollViewerPropertyChanged;
        DetachedFromVisualTree += OnDetached;
    }

    /// <summary>ScrollViewer shown when the details page is active.</summary>
    public ScrollViewer ScrollViewer => DetailsScrollViewer;

    private void OnDetailsScrollViewerPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property != Visual.IsVisibleProperty ||
            !eventArgs.GetNewValue<bool>())
        {
            return;
        }

        DetailsScrollViewer.Offset = new Vector(0, 0);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) =>
        DetailsScrollViewer.PropertyChanged -= OnDetailsScrollViewerPropertyChanged;
}
