using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Rezui.Models;

namespace Rezui.Views;

/// <summary>
/// Home page: continue-watching hero, mood quick-search tiles and the
/// masonry of recently opened titles. Extracted from MainWindow so the shell
/// stays a thin host while this owns the home-specific interactions
/// (hover-to-load card metadata and the responsive hero title sizing).
/// </summary>
public partial class HomePage : UserControl
{
    public HomePage() => InitializeComponent();

    // Bubbled from the ItemsControl that hosts HomePosterCardTemplate items.
    // Hovering a card prefetches its cached metadata so opening it is instant.
    private void HomeCard_OnPointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        if (eventArgs.Source is not Visual source)
        {
            return;
        }

        if (source.DataContext is HomeMediaCardItem { } card &&
            card.LoadMetadataCommand.CanExecute(null))
        {
            card.LoadMetadataCommand.Execute(null);
        }
    }

    // Picks a larger font when the title fits one line, wraps otherwise.
    // Mirrors the original behaviour from MainWindow before extraction.
    private void ContinueTitle_OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (sender is not TextBlock title ||
            string.IsNullOrWhiteSpace(title.Text) ||
            eventArgs.NewSize.Width <= 0)
        {
            return;
        }

        const double largeFontSize = 54;
        const double regularFontSize = 34;
        var singleLineProbe = new TextBlock
        {
            Text = title.Text,
            FontFamily = title.FontFamily,
            FontWeight = title.FontWeight,
            FontStyle = title.FontStyle,
            FontStretch = title.FontStretch,
            FontSize = largeFontSize,
            TextWrapping = TextWrapping.NoWrap
        };
        singleLineProbe.Measure(
            new Size(double.PositiveInfinity, double.PositiveInfinity));

        var fitsOnOneLine =
            singleLineProbe.DesiredSize.Width <= eventArgs.NewSize.Width;
        var targetFontSize = fitsOnOneLine ? largeFontSize : regularFontSize;
        if (Math.Abs(title.FontSize - targetFontSize) < 0.01)
        {
            return;
        }

        title.FontSize = targetFontSize;
        title.LineHeight = fitsOnOneLine ? 60 : 40;
        title.MaxHeight = fitsOnOneLine ? 64 : 82;
        title.TextWrapping = fitsOnOneLine
            ? TextWrapping.NoWrap
            : TextWrapping.Wrap;
    }
}
