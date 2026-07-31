using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Rezui.Views;
using Xunit;

namespace Rezui.Tests;

public sealed class MainWindowSmokeTests
{
    [AvaloniaFact]
    public async Task MainWindowCanBuildItsVisualTree()
    {
        var window = new MainWindow();
        var carousel = window.FindControl<Carousel>("StartupWizardCarousel");
        var emailInput = window.FindControl<TextBox>("LoginEmailInput");
        var passwordInput = window.FindControl<TextBox>("LoginPasswordInput");
        var loginButton = window.FindControl<Button>("LoginSubmitButton");
        var mirrorUseButton = window.FindControl<Button>("MirrorUseButton");

        Assert.NotNull(window.Content);
        Assert.NotNull(carousel);
        Assert.NotNull(emailInput);
        Assert.NotNull(passwordInput);
        Assert.NotNull(loginButton);
        Assert.NotNull(mirrorUseButton);

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
