using System.Threading.Channels;

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
    private readonly Channel<LibrarySyncReason> _requests;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _workerTask;
    private volatile bool _disposed;

    public LibrarySyncWorker(ILibrarySnapshotProvider provider)
    {
        _provider = provider;
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
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _requests.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_requests.Reader.TryRead(out _))
                {
                }

                try
                {
                    var snapshot = await _provider.GetLibraryAsync(cancellationToken);
                    SnapshotChanged?.Invoke(snapshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
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
