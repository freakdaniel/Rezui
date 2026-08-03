using Avalonia.Media.Imaging;
using Rezui.Models;
using Xunit;

namespace Rezui.Tests;

public sealed class DeferredImageSourceTests
{
    [Fact]
    public async Task ValueStartsFactoryOnceAndOnlyWhenRequested()
    {
        var calls = 0;
        var completion = new TaskCompletionSource<Bitmap?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DeferredImageSource(() =>
        {
            Interlocked.Increment(ref calls);
            return completion.Task;
        });

        Assert.Equal(0, Volatile.Read(ref calls));

        var first = source.Value;
        var second = source.Value;
        Assert.Same(first, second);
        Assert.Equal(1, Volatile.Read(ref calls));

        completion.SetResult(null);
        Assert.Null(await first);
        Assert.Null(await second);
        Assert.Equal(1, Volatile.Read(ref calls));
    }
}
