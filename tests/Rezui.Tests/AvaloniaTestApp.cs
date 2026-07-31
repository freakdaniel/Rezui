using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Rezui.Tests.AvaloniaTestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rezui.Tests;

public static class AvaloniaTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
