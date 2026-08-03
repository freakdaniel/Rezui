using System.Diagnostics;
using System.Threading.Channels;
using Serilog;

namespace Rezui.Services;

public enum LibrarySyncReason
{
    SessionRestored,
    LibraryOpened,
    WindowActivated
}

public sealed class LibrarySyncWorker : IDisposable
{
    private readonly ILibrarySnapshotProvider _provider;
    private readonly ILogger _logger;
    private readonly Channel<LibrarySyncReason> _requests;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _workerTask;
    private volatile bool _disposed;

    public LibrarySyncWorker(
        ILibrarySnapshotProvider provider,
        ILogger? logger = null)
    {
        _provider = provider;
        _logger = logger ?? Log.ForContext<LibrarySyncWorker>();
        _requests = Channel.CreateBounded<LibrarySyncReason>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
        _workerTask = RunAsync(_shutdown.Token);
    }

    public event Action<AccountLibrarySnapshot>? SnapshotChanged;

    public event Action<Exception>? SyncFailed;

    public void RequestRefresh(LibrarySyncReason reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requests.Writer.TryWrite(reason);
        _logger.Debug("Library synchronization requested because {SyncReason}", reason);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _requests.Reader.WaitToReadAsync(cancellationToken))
            {
                var reason = LibrarySyncReason.SessionRestored;
                while (_requests.Reader.TryRead(out var nextReason))
                {
                    reason = nextReason;
                }

                try
                {
                    var startedAt = Stopwatch.GetTimestamp();
                    var snapshot = await _provider.GetLibraryAsync(cancellationToken);
                    _logger.Information(
                        "Library synchronized because {SyncReason} in {DurationMs:0.0} ms; continue watching={ContinueWatchingCount}, folders={FolderCount}",
                        reason,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                        snapshot.ContinueWatching.Count,
                        snapshot.BookmarkFolders.Count);
                    SnapshotChanged?.Invoke(snapshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.Error(
                        exception,
                        "Library synchronization failed because {SyncReason}",
                        reason);
                    SyncFailed?.Invoke(exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requests.Writer.TryComplete();
        _shutdown.Cancel();
        _ = _workerTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        _shutdown.Dispose();
    }
}
