using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Rezui.Models;
using Rezui.Services;
using Rezui.ViewModels;
using Rezui.Views;
using Xunit;

namespace Rezui.Tests;

public sealed class StartupFlowTests
{
    [AvaloniaFact]
    public async Task StartupShowsOnlyLoadingThenOnlyLoginWhenSessionIsMissing()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        var window = new MainWindow
        {
            DataContext = viewModel
        };
        var loading = Assert.IsType<Grid>(
            window.FindNamed<Grid>("StartupLoadingContent"));
        var wizard = Assert.IsType<StackPanel>(
            window.FindNamed<StackPanel>("StartupWizardContent"));
        var overlay = Assert.IsType<Grid>(
            window.FindNamed<Grid>("StartupOverlay"));
        var brand = Assert.IsType<Grid>(
            window.FindNamed<Grid>("LoginBrandComposition"));
        var loginFox = Assert.IsType<Image>(
            window.FindNamed<Image>("LoginFoxLogo"));
        var loadingLogo = Assert.IsType<Image>(
            window.FindNamed<Image>("StartupLoadingLogo"));
        var mirrorAnchor = Assert.IsType<Button>(
            window.FindNamed<Button>("MirrorAnchorButton"));
        var startupSpinner = Assert.IsType<TextBlock>(
            window.FindNamed<TextBlock>("StartupSpinner"));

        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(loading.IsVisible);
            Assert.False(wizard.IsVisible);

            await viewModel.Initialization;
            window.UpdateLayout();

            Assert.False(loading.IsVisible);
            Assert.True(wizard.IsVisible);
            Assert.True(viewModel.IsStartupAuthenticationRequired);

            viewModel.IsLoginFoxVisible = true;
            window.UpdateLayout();
            await Task.Delay(220);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);

            AssertPointsAreClose(
                GetCenter(brand, window) + new Vector(-92, 0),
                GetCenter(loginFox, window));

            viewModel.IsLoginFoxMotionSuppressed = false;
            window.UpdateLayout();
            await Task.Delay(20);
            viewModel.IsLoginFoxCentered = true;
            window.UpdateLayout();
            await Task.Delay(120);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(20);

            var initialFoxCenter = GetCenter(brand, window) + new Vector(-92, 0);
            var finalFoxCenter = GetCenter(overlay, window);
            var movingFoxCenter = GetCenter(loginFox, window);
            Assert.InRange(
                movingFoxCenter.Y,
                initialFoxCenter.Y + 1,
                finalFoxCenter.Y - 1);

            await Task.Delay(320);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(60);

            Assert.Equal(loadingLogo.Bounds.Size, loginFox.Bounds.Size);
            AssertPointsAreClose(GetCenter(overlay, window), GetCenter(loginFox, window));

            viewModel.IsLoginFoxMotionSuppressed = true;
            viewModel.IsLoginFoxCentered = false;
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            AssertPointsAreClose(
                GetCenter(brand, window) + new Vector(-92, 0),
                GetCenter(loginFox, window));

            viewModel.IsLoginSuccessTransition = true;
            window.UpdateLayout();
            await Task.Delay(320);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);

            Assert.Equal(0, mirrorAnchor.Opacity);
            Assert.False(mirrorAnchor.IsHitTestVisible);

            viewModel.IsStartupLoading = true;
            window.UpdateLayout();
            Assert.True(startupSpinner.Opacity < 1);
            await Task.Delay(320);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);
            Assert.Equal(1, startupSpinner.Opacity);

            viewModel.IsShellVisible = true;
            viewModel.IsStartupVisible = false;
            window.UpdateLayout();
            Assert.True(overlay.Opacity > 0);
            await Task.Delay(360);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(50);
            Assert.Equal(0, overlay.Opacity);
        }
        finally
        {
            window.Close();
        }
    }

    private static Point GetCenter(Control control, Visual relativeTo)
    {
        var localCenter = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        return control.TranslatePoint(localCenter, relativeTo) ?? default;
    }

    private static void AssertPointsAreClose(Point expected, Point actual)
    {
        Assert.True(
            Math.Abs(expected.X - actual.X) <= 1
            && Math.Abs(expected.Y - actual.Y) <= 1,
            $"Expected {expected}, actual {actual}");
    }

    [Fact]
    public async Task StartupUsesDefaultMirrorAndRequestsLoginWhenSettingsAreMissing()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.True(viewModel.IsStartupVisible);
        Assert.True(viewModel.IsStartupPresented);
        Assert.False(viewModel.IsStartupLoading);
        Assert.True(viewModel.IsStartupAuthenticationRequired);
        Assert.Equal(RezkaMirrors.Primary, viewModel.Origin);
        Assert.Equal(RezkaMirrors.Defaults.Count, viewModel.MirrorStatuses.Count);
        Assert.True(viewModel.CanLogin);
        Assert.Equal("Зеркало: автовыбор", viewModel.MirrorSelectorLabel);
    }

    [Fact]
    public async Task StartupSelectsAvailableMirrorWithLowestLatency()
    {
        using var fixture = new TestStartupFixture();
        fixture.Mirrors.Latencies[RezkaMirrors.Primary] = 45;
        fixture.Mirrors.Latencies["https://rezka.fi"] = 9;
        fixture.Mirrors.Latencies["https://hdrezka.cm"] = 28;
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.Equal("https://rezka.fi", viewModel.Origin);
        Assert.True(viewModel.IsAutoMirrorSelection);
        Assert.DoesNotContain(viewModel.MirrorStatuses, item => item.IsSelected);
    }

    [Fact]
    public void StartupPrefersSavedAvailableMirrorWhenSessionCookiesExist()
    {
        var settings = new AppSettings
        {
            Origin = "https://rezka.fi"
        };
        var auth = new AuthState
        {
            Cookies =
            {
                ["dle_user_id"] = "user-id",
                ["dle_password"] = "password-hash"
            }
        };
        var fastest = new MirrorStatusItem(
            RezkaMirrors.Primary,
            "primary",
            5,
            true,
            false,
            false);
        var saved = new MirrorStatusItem(
            "https://rezka.fi",
            "rezka.fi",
            50,
            true,
            false,
            false);

        var selected = MainWindowViewModel.FindRestorableSessionMirror(
            settings,
            auth,
            new[] { fastest, saved });

        Assert.Same(saved, selected);
    }

    [Fact]
    public async Task StartupBlocksLoginWhenEveryMirrorIsUnavailable()
    {
        using var fixture = new TestStartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.False(viewModel.CanLogin);
        Assert.True(viewModel.IsStartupAuthenticationRequired);
        Assert.All(viewModel.MirrorStatuses, item => Assert.False(item.IsAvailable));
    }

    [Fact]
    public async Task MirrorWizardMovesForwardAndBackFromTheLoginStep()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;

        Assert.True(viewModel.IsLoginWizardStep);
        Assert.False(viewModel.IsMirrorWizardStep);

        await viewModel.OpenMirrorWizardCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.StartupWizardStep);
        Assert.True(viewModel.IsMirrorWizardStep);

        await viewModel.CloseMirrorWizardCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.StartupWizardStep);
        Assert.True(viewModel.IsLoginWizardStep);
    }

    [Fact]
    public async Task RapidOppositeWizardClicksCannotInterruptActiveTransition()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;

        var opening = viewModel.OpenMirrorWizardCommand.ExecuteAsync(null);
        viewModel.CloseMirrorWizardCommand.Execute(null);
        viewModel.OpenMirrorWizardCommand.Execute(null);
        viewModel.CloseMirrorWizardCommand.Execute(null);

        await opening;

        Assert.True(viewModel.IsMirrorWizardStep);
        Assert.False(viewModel.IsWizardTransitioning);

        await viewModel.CloseMirrorWizardCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoginWizardStep);
        Assert.False(viewModel.IsWizardTransitioning);
    }

    [AvaloniaFact]
    public async Task RepeatedWizardNavigationLeavesBothPagesFullyOpaque()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        var window = new MainWindow
        {
            DataContext = viewModel
        };
        var loginPage = Assert.IsType<Grid>(
            window.FindNamed<Grid>("LoginWizardPage"));
        var mirrorPage = Assert.IsType<Grid>(
            window.FindNamed<Grid>("MirrorWizardPage"));

        window.Show();
        try
        {
            await viewModel.Initialization;

            for (var iteration = 0; iteration < 3; iteration++)
            {
                await viewModel.OpenMirrorWizardCommand.ExecuteAsync(null);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);
                window.UpdateLayout();
                Assert.Equal(1, mirrorPage.Opacity);
                Assert.Null(mirrorPage.RenderTransform);

                await viewModel.CloseMirrorWizardCommand.ExecuteAsync(null);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);
                window.UpdateLayout();
                Assert.Equal(1, loginPage.Opacity);
                Assert.Null(loginPage.RenderTransform);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task ChangingMirrorSelectionKeepsRowsAliveForStateTransitions()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        var first = viewModel.MirrorStatuses[0];
        var second = viewModel.MirrorStatuses[1];

        viewModel.SelectMirrorCommand.Execute(first);
        viewModel.SelectMirrorCommand.Execute(second);

        Assert.Same(first, viewModel.MirrorStatuses[0]);
        Assert.Same(second, viewModel.MirrorStatuses[1]);
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }

    [Fact]
    public async Task MirrorWizardCannotOpenWhileConnectionCheckIsRunning()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        viewModel.IsMirrorCheckRunning = true;

        viewModel.OpenMirrorWizardCommand.Execute(null);

        Assert.Equal(0, viewModel.StartupWizardStep);
    }

    [Fact]
    public async Task InvalidLoginUsesStatusAreaWithoutChangingPageCopy()
    {
        using var fixture = new TestStartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        var title = viewModel.StartupTitle;
        var message = viewModel.StartupMessage;

        viewModel.Login = "not-an-email";
        viewModel.Password = "password";
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(title, viewModel.StartupTitle);
        Assert.Equal(message, viewModel.StartupMessage);
        Assert.Equal("Введите корректную почту и пароль", viewModel.LoginStatusMessage);
        Assert.True(viewModel.IsLoginStatusVisible);
        Assert.False(viewModel.IsLoginRunning);
    }

    [Fact]
    public async Task AvailableCustomMirrorIsUsedAndPersisted()
    {
        using var fixture = new TestStartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        fixture.Mirrors.AllUnavailable = false;
        viewModel.OpenMirrorWizardCommand.Execute(null);
        viewModel.CustomMirror = "custom.example.com";

        await viewModel.UseCustomMirrorCommand.ExecuteAsync(null);
        var settings = await fixture.Settings.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.True(viewModel.CanLogin);
        Assert.True(viewModel.IsMirrorStatusVisible);
        Assert.Equal("https://custom.example.com", viewModel.Origin);
        Assert.Equal("Зеркало: custom.example.com", viewModel.MirrorSelectorLabel);
        Assert.True(viewModel.IsLoginWizardStep);
        Assert.Contains("https://custom.example.com", settings.CustomMirrors);
    }

    [Fact]
    public async Task UnavailableCustomMirrorIsNotAddedOrSelected()
    {
        using var fixture = new TestStartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        viewModel.OpenMirrorWizardCommand.Execute(null);
        viewModel.CustomMirror = "offline.example.com";

        await viewModel.UseCustomMirrorCommand.ExecuteAsync(null);
        var settings = await fixture.Settings.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(viewModel.CanLogin);
        Assert.True(viewModel.IsMirrorStatusVisible);
        Assert.Equal(string.Empty, viewModel.Origin);
        Assert.DoesNotContain("https://offline.example.com", settings.CustomMirrors);
        Assert.DoesNotContain(
            viewModel.MirrorStatuses,
            item => item.Origin == "https://offline.example.com");
    }

    [Fact]
    public async Task ReachableNonRezkaSiteIsNotAddedOrSelected()
    {
        using var fixture = new TestStartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        fixture.Mirrors.AllUnavailable = false;
        fixture.Mirrors.RejectRezkaValidation = true;
        viewModel.OpenMirrorWizardCommand.Execute(null);
        viewModel.CustomMirror = "unrelated.example.com";

        await viewModel.UseCustomMirrorCommand.ExecuteAsync(null);
        var settings = await fixture.Settings.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("Адрес доступен, но не является зеркалом HDRezka", viewModel.MirrorStatusMessage);
        Assert.True(viewModel.IsMirrorStatusVisible);
        Assert.Equal(string.Empty, viewModel.Origin);
        Assert.DoesNotContain("https://unrelated.example.com", settings.CustomMirrors);
        Assert.DoesNotContain(
            viewModel.MirrorStatuses,
            item => item.Origin == "https://unrelated.example.com");
    }

    [Fact]
    public async Task StartupRequiresLoginWhenSessionCookiesAreMissing()
    {
        using var fixture = new TestStartupFixture();
        await fixture.Settings.SaveAsync(
            new AppSettings
            {
                Origin = "https://example.com"
            },
            TestContext.Current.CancellationToken);
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.True(viewModel.IsStartupVisible);
        Assert.True(viewModel.IsStartupAuthenticationRequired);
        Assert.False(viewModel.IsShellVisible);
    }
}
