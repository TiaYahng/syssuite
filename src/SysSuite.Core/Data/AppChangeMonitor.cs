using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed class AppChangeMonitor : IAppChangeMonitor
{
    private readonly IUninstallEnumerationService enumerationService;
    private readonly TimeSpan pollInterval;
    private readonly SemaphoreSlim pollLock = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? pollTask;
    private readonly UsnJournalMonitor usnJournalMonitor = new();

    public AppChangeMonitor(IUninstallEnumerationService enumerationService, TimeSpan? pollInterval = null)
    {
        this.enumerationService = enumerationService;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
    }

    public event EventHandler<IReadOnlyList<AppRecord>>? AppListChanged;

    public bool IsRunning => cancellation is not null;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        usnJournalMonitor.FileChanged += OnUsnFileChanged;
        usnJournalMonitor.Start();
        pollTask = Task.Run(() => PollAsync(cancellation.Token));
    }

    public void StopMonitoring()
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            pollTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        usnJournalMonitor.FileChanged -= OnUsnFileChanged;
        usnJournalMonitor.Stop();
        cancellation.Dispose();
        cancellation = null;
        pollTask = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var previousKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollInterval, cancellationToken);
                if (!await pollLock.WaitAsync(0, cancellationToken))
                {
                    continue;
                }

                try
                {
                    var result = await enumerationService.RefreshAsync(cancellationToken);
                    if (!result.IsSuccess || result.Value is null)
                    {
                        continue;
                    }

                    var currentKeys = result.Value.Select(app => app.StableKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!currentKeys.SetEquals(previousKeys))
                    {
                        previousKeys = currentKeys;
                        AppListChanged?.Invoke(this, result.Value);
                    }
                }
                finally
                {
                    pollLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
            }
        }
    }

    private bool disposed;

    private async void OnUsnFileChanged(object? sender, string fileName)
    {
        try
        {
            await Task.Delay(500);
            if (!await pollLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                var result = await enumerationService.RefreshAsync();
                if (!result.IsSuccess || result.Value is null)
                {
                    return;
                }

                AppListChanged?.Invoke(this, result.Value);
            }
            finally
            {
                pollLock.Release();
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        StopMonitoring();
        pollLock.Dispose();
        usnJournalMonitor.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
