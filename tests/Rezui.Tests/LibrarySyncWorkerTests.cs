using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class LibrarySyncWorkerTests
{
    [Fact]
    public async Task RefreshRequestPublishesProviderSnapshot()
    {
        var expected = new AccountLibrarySnapshot([], []);
        var provider = new StubLibrarySnapshotProvider(expected);
        using var worker = new LibrarySyncWorker(provider);
        var received = new TaskCompletionSource<AccountLibrarySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        worker.SnapshotChanged += snapshot => received.TrySetResult(snapshot);

        worker.RequestRefresh(LibrarySyncReason.LibraryOpened);

        var actual = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Same(expected, actual);
        Assert.Equal(1, provider.RequestCount);
    }

    private sealed class StubLibrarySnapshotProvider(AccountLibrarySnapshot snapshot)
        : ILibrarySnapshotProvider
    {
        public int RequestCount { get; private set; }

        public Task<AccountLibrarySnapshot> GetLibraryAsync(
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return Task.FromResult(snapshot);
        }
    }
}
