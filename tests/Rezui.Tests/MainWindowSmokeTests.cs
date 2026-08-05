using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Rezui.Controls;
using Rezui.Models;
using Rezui.ViewModels;
using Rezui.Views;
using Xunit;

namespace Rezui.Tests;

public sealed class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void MoodCardKeepsCaptionAndTitleAboveTheArtwork()
    {
        var templateOwner = new MainWindow();
        var template = Assert.IsAssignableFrom<IDataTemplate>(
            templateOwner.Resources["HomeMoodCardTemplate"]);
        var item = new QuickSearchItem(
            "Детективы",
            "детектив",
            "Запутанные",
            new SolidColorBrush(Color.Parse("#8B566E")),
            new DeferredImageSource(() => Task.FromResult<Bitmap?>(null)),
            "fingerprint",
            2);
        var presenter = new ContentControl
        {
            Content = item,
            ContentTemplate = template,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        var host = new Window { Width = 260, Height = 260, Content = presenter };

        host.Show();
        try
        {
            host.UpdateLayout();
            var label = presenter.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-mood-label"));
            var runs = label.Inlines!.OfType<Run>().ToArray();

            Assert.Equal("Запутанные детективы", item.MoodLabel);
            Assert.Equal(3, runs.Length);
            Assert.Equal("Запутанные", runs[0].Text);
            Assert.Equal(" ", runs[1].Text);
            Assert.Equal("детективы", runs[2].Text);
            Assert.Equal(FontWeight.Medium, runs[0].FontWeight);
            Assert.Equal(FontWeight.Bold, runs[2].FontWeight);
            Assert.True(label.IsEffectivelyVisible);
            Assert.True(label.Bounds.Height >= 19);
            Assert.True(
                label.Bounds.Width >= 100,
                $"label bounds={label.Bounds}, desired={label.DesiredSize}");
            Assert.Equal(1, label.Opacity);
            Assert.Equal(Color.Parse("#D9FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(runs[0].Foreground).Color);
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(runs[2].Foreground).Color);
            var button = presenter.GetVisualDescendants()
                .OfType<Button>()
                .Single(control => control.Classes.Contains("home-mood-card"));
            Assert.Equal(new Size(156, 156), button.Bounds.Size);
            using var frame = new RenderTargetBitmap(new PixelSize(260, 260));
            frame.Render(host);
            using var pixels = new WriteableBitmap(
                new PixelSize(260, 260),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using var framebuffer = pixels.Lock();
            frame.CopyPixels(framebuffer);
            var lightTextPixels = 0;
            for (var y = 145; y < 196; y++)
            {
                for (var x = 52; x < 190; x++)
                {
                    var offset = y * framebuffer.RowBytes + x * 4;
                    var blue = Marshal.ReadByte(framebuffer.Address, offset);
                    var green = Marshal.ReadByte(framebuffer.Address, offset + 1);
                    var red = Marshal.ReadByte(framebuffer.Address, offset + 2);
                    if (red > 210 && green > 210 && blue > 210)
                    {
                        lightTextPixels++;
                    }
                }
            }

            Assert.True(
                lightTextPixels >= 40,
                $"Expected rendered text pixels, found {lightTextPixels}.");
        }
        finally
        {
            host.Close();
            templateOwner.Close();
        }
    }

    [AvaloniaFact]
    public void MoodArtworkIsEmbeddedAndDecodesWithTransparency()
    {
        var artworkKeys = new[]
        {
            "pencil",
            "book",
            "home",
            "fedora",
            "cat",
            "fingerprint",
            "lion",
            "bulb"
        };

        foreach (var key in artworkKeys)
        {
            using var stream = AssetLoader.Open(
                new Uri($"avares://Rezui/Assets/3D/{key}.png"));
            using var bitmap = Bitmap.DecodeToWidth(
                stream,
                216,
                BitmapInterpolationMode.HighQuality);

            Assert.Equal(216, bitmap.PixelSize.Width);
            Assert.Equal(216, bitmap.PixelSize.Height);
            Assert.NotNull(bitmap.Format);
        }
    }

    [AvaloniaFact]
    public async Task HomeHistoryCardKeepsThePosterStableAndRevealsShadowMetadata()
    {
        var templateOwner = new MainWindow();
        var template = Assert.IsAssignableFrom<IDataTemplate>(
            templateOwner.Resources["HomePosterCardTemplate"]);
        var media = new MediaCardItem(
            "Тестовый фильм",
            new Uri("https://example.com/films/test.html"),
            new DeferredImageSource(() => Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null)),
            "Фильм",
            () => Task.CompletedTask);
        var card = new HomeMediaCardItem(
            media,
            0,
            false,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);
        card.RatingLabel = "8.5";
        card.YearLabel = "2016";
        card.QualityLabel = "4K";
        var presenter = new ContentControl
        {
            Width = 220,
            Height = 330,
            Content = card,
            ContentTemplate = template,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        var host = new Window { Width = 360, Height = 480, Content = presenter };

        host.Show();
        try
        {
            host.UpdateLayout();
            var root = presenter.GetVisualDescendants()
                .OfType<Grid>()
                .First(control => control.Classes.Contains("home-media-card"));
            var initialBounds = root.Bounds;
            var poster = presenter.GetVisualDescendants()
                .OfType<Image>()
                .First(control => control.Classes.Contains("home-card-poster"));
            var frame = presenter.GetVisualDescendants()
                .OfType<Border>()
                .First(control => control.Classes.Contains("home-card-frame"));
            var title = presenter.GetVisualDescendants()
                .OfType<TextBlock>()
                .First(control => control.Classes.Contains("home-card-title"));
            var motionShadow = presenter.GetVisualDescendants()
                .OfType<Border>()
                .First(control => control.Classes.Contains("home-card-shadow-motion"));
            var details = presenter.GetVisualDescendants()
                .OfType<ProportionalReveal>()
                .First(control => control.Classes.Contains("home-card-details"));
            var actions = presenter.GetVisualDescendants()
                .OfType<Grid>()
                .First(control => control.Classes.Contains("home-card-actions"));
            var navigate = presenter.GetVisualDescendants()
                .OfType<Button>()
                .Single(control => control.Classes.Contains("home-card-navigate"));
            var bookmark = presenter.GetVisualDescendants()
                .OfType<Button>()
                .Single(control => control.Classes.Contains("home-card-bookmark"));
            var navigateIcon = navigate.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-card-action-icon"));
            var navigateLabel = navigate.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-card-navigate-label"));
            var bookmarkOutline = bookmark.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-card-bookmark-outline"));
            var bookmarkFilled = bookmark.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-card-bookmark-filled"));
            var ratingValue = presenter.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-card-rating-value"));
            var ratingStar = presenter.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("home-rating-star"));
            Assert.Null(ToolTip.GetTip(root));
            Assert.Null(root.RenderTransform);
            Assert.Equal(new Avalonia.Thickness(-4), poster.Margin);
            Assert.Equal(new Avalonia.CornerRadius(0), frame.CornerRadius);
            var posterScale = Assert.IsType<ScaleTransform>(poster.RenderTransform);
            Assert.Equal(1.045, posterScale.ScaleX);
            Assert.Equal(0, details.RevealProgress);
            Assert.Equal(0, details.Bounds.Height);
            Assert.Contains(actions, details.GetVisualDescendants().OfType<Grid>());
            Assert.Equal("arrow_forward", navigateIcon.Text);
            Assert.Equal("Перейти", navigateLabel.Text);
            Assert.Equal("bookmark_border", bookmarkOutline.Text);
            Assert.Equal("bookmark", bookmarkFilled.Text);
            Assert.Equal(0, Grid.GetColumn(bookmark));
            Assert.Equal(1, Grid.GetColumn(navigate));
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, navigate.HorizontalContentAlignment);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, navigate.VerticalContentAlignment);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, bookmark.HorizontalContentAlignment);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, bookmark.VerticalContentAlignment);
            Assert.Equal(new Size(22, 22), navigateIcon.Bounds.Size);
            Assert.Equal(new Size(22, 22), bookmarkOutline.Bounds.Size);
            Assert.Equal(new Size(22, 22), bookmarkFilled.Bounds.Size);
            Assert.Same(navigate.Theme, bookmark.Theme);
            Assert.NotNull(navigate.Theme);
            Assert.Equal(Brushes.Transparent, bookmark.Background);
            Assert.Null(bookmark.Transitions);
            Assert.Equal(1, bookmarkOutline.Opacity);
            Assert.Equal(0, bookmarkFilled.Opacity);
            Assert.Equal(0, navigateLabel.Opacity);
            var initialNavigateIconCenter = navigateIcon.TranslatePoint(new Point(11, 11), navigate);
            Assert.NotNull(initialNavigateIconCenter);
            Assert.InRange(initialNavigateIconCenter.Value.X, 16.5, 17.5);
            Assert.InRange(initialNavigateIconCenter.Value.Y, 16.5, 17.5);
            var titleLeft = title.TranslatePoint(default, root);
            var bookmarkVisualLeft = bookmarkOutline.TranslatePoint(default, root);
            Assert.NotNull(titleLeft);
            Assert.NotNull(bookmarkVisualLeft);
            Assert.InRange(Math.Abs(titleLeft.Value.X - bookmarkVisualLeft.Value.X), 0, 0.5);
            Assert.Equal("8.5", ratingValue.Text);
            Assert.Equal(card.MetadataFontFamily, ratingValue.FontFamily);
            Assert.Equal(FontWeight.SemiBold, ratingValue.FontWeight);
            Assert.Equal(Brushes.White, ratingStar.Foreground);
            Assert.DoesNotContain(
                presenter.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text == card.Category);
            Assert.DoesNotContain(
                presenter.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text is "2016" or "4K");

            host.MouseMove(new Point(100, 100), RawInputModifiers.None);
            await Task.Delay(90);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(8);
            host.UpdateLayout();

            Assert.InRange(details.RevealProgress, 0.001, 0.999);

            await Task.Delay(760);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(48);
            host.UpdateLayout();

            Assert.True(root.IsPointerOver);
            Assert.Equal(1, details.RevealProgress);
            Assert.True(motionShadow.Opacity > 0.9);
            Assert.Equal(34, actions.Bounds.Height);
            Assert.Equal(initialBounds.Size, root.Bounds.Size);

            var navigateCenter = navigate.TranslatePoint(new Point(17, 17), host);
            Assert.NotNull(navigateCenter);
            host.MouseMove(navigateCenter.Value, RawInputModifiers.None);
            await Task.Delay(500);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(32);
            host.UpdateLayout();

            Assert.True(navigate.IsPointerOver);
            Assert.InRange(navigate.Width, 95.5, 96.5);
            Assert.True(navigateLabel.Opacity > 0.9);
            var expandedLabelLeft = navigateLabel.TranslatePoint(default, navigate);
            var expandedArrowLeft = navigateIcon.TranslatePoint(default, navigate);
            Assert.NotNull(expandedLabelLeft);
            Assert.NotNull(expandedArrowLeft);
            Assert.InRange(expandedLabelLeft.Value.X, 11.5, 12.5);
            Assert.InRange(
                expandedArrowLeft.Value.X - (expandedLabelLeft.Value.X + navigateLabel.Bounds.Width),
                6,
                20);

            var bookmarkCenter = bookmark.TranslatePoint(new Point(17, 17), host);
            Assert.NotNull(bookmarkCenter);
            host.MouseMove(bookmarkCenter.Value, RawInputModifiers.None);
            await Task.Delay(500);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(32);
            host.UpdateLayout();

            Assert.True(bookmark.IsPointerOver);
            Assert.Equal(34, bookmark.Width);
            Assert.Equal(Brushes.Transparent, bookmark.Background);
            Assert.True(bookmarkOutline.Opacity < 0.1);
            Assert.True(bookmarkFilled.Opacity > 0.9);

            host.MouseDown(bookmarkCenter.Value, MouseButton.Left, RawInputModifiers.None);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Assert.Equal(Brushes.Transparent, bookmark.Background);
            host.MouseUp(bookmarkCenter.Value, MouseButton.Left, RawInputModifiers.None);
            card.IsSaved = true;
            await Task.Delay(40);
            host.UpdateLayout();
            Assert.True(bookmarkFilled.Opacity > 0.9);

            host.MouseMove(new Point(350, 450), RawInputModifiers.None);
            await Task.Delay(60);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            host.UpdateLayout();

            Assert.InRange(details.RevealProgress, 0.001, 0.999);

            await Task.Delay(760);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(48);
            host.UpdateLayout();

            Assert.False(root.IsPointerOver);
            Assert.Equal(0, details.RevealProgress);
            Assert.Equal(0, details.Bounds.Height);
        }
        finally
        {
            host.Close();
            templateOwner.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindowCanBuildItsVisualTree()
    {
        var window = new MainWindow();
        var carousel = window.FindControl<Carousel>("StartupWizardCarousel");
        var emailInput = window.FindControl<TextBox>("LoginEmailInput");
        var passwordInput = window.FindControl<TextBox>("LoginPasswordInput");
        var loginButton = window.FindControl<Button>("LoginSubmitButton");
        var mirrorUseButton = window.FindControl<Button>("MirrorUseButton");
        var homeRecentCollection = window.FindControl<Grid>("HomeRecentCollection");

        Assert.NotNull(window.Content);
        Assert.NotNull(carousel);
        Assert.NotNull(emailInput);
        Assert.NotNull(passwordInput);
        Assert.NotNull(loginButton);
        Assert.NotNull(mirrorUseButton);
        Assert.NotNull(homeRecentCollection);

        window.Show();
        try
        {
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.UpdateLayout();
            var initialBounds = carousel.Bounds;

            AssertContrastButton(
                loginButton,
                Color.Parse("#FF1C1C1F"),
                Colors.White);
            AssertContrastButton(
                mirrorUseButton,
                Color.Parse("#FF1C1C1F"),
                Colors.White);

            await AssertLoginInputFocusState(emailInput, window);
            await AssertLoginInputFocusState(passwordInput, window);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            await Task.Delay(80);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
            window.UpdateLayout();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            AssertContrastButton(loginButton, Colors.White, Colors.Black);
            AssertContrastButton(mirrorUseButton, Colors.White, Colors.Black);

            emailInput.Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(emailInput.IsFocused);

            passwordInput.Focus();
            window.MouseDown(new Avalonia.Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Avalonia.Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            Assert.False(passwordInput.IsFocused);

            carousel.SelectedIndex = 1;
            window.UpdateLayout();

            Assert.Equal(460, carousel.Bounds.Height);
            Assert.Equal(initialBounds, carousel.Bounds);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task AssertLoginInputFocusState(TextBox input, Window window)
    {
        var icon = Assert.IsType<TextBlock>(input.InnerLeftContent);
        var placeholder = input.GetTemplateDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "PART_Placeholder");
        var borderBrush = input.BorderBrush;

        Assert.Equal(placeholder.Foreground, icon.Foreground);
        Assert.True(placeholder.IsVisible);

        input.Focus();
        window.UpdateLayout();
        await Task.Delay(320);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(60);

        Assert.True(input.IsFocused);
        Assert.True(placeholder.IsVisible);
        Assert.Equal(0, placeholder.Opacity);
        Assert.Equal(input.Foreground, icon.Foreground);
        Assert.Equal(input.Foreground, input.CaretBrush);
        Assert.Equal(1, icon.Opacity);
        Assert.Equal(borderBrush, input.BorderBrush);

        input.Text = "filled";
        window.FocusManager?.Focus(null);
        window.UpdateLayout();

        Assert.False(input.IsFocused);
        Assert.Equal(input.Foreground, icon.Foreground);
        Assert.Equal(1, icon.Opacity);

        input.Text = string.Empty;
    }

    private static void AssertContrastButton(
        Button button,
        Color expectedBackground,
        Color expectedForeground)
    {
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background);
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground);

        Assert.Equal(expectedBackground, background.Color);
        Assert.Equal(expectedForeground, foreground.Color);
    }
}
