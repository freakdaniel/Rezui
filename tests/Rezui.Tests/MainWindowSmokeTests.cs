using Avalonia.Headless.XUnit;
using Rezui.Views;
using Xunit;

namespace Rezui.Tests;

public sealed class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void MainWindowCanBuildItsVisualTree()
    {
        var window = new MainWindow();

        Assert.NotNull(window.Content);
    }
}
