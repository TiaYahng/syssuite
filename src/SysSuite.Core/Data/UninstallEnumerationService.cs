using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed class UninstallEnumerationService : IUninstallEnumerationService, IAsyncDisposable
{
    private readonly ISharedDatabaseService databaseService;
    private readonly RegistryAppEnumerator registryEnumerator;
    private readonly TaskCoordinator taskQueue = new();

    public UninstallEnumerationService(
        ISharedDatabaseService databaseService,
        bool includeSystemComponents = false)
    {
        this.databaseService = databaseService;
        registryEnumerator = new RegistryAppEnumerator(includeSystemComponents);
    }

    public async Task<Result<IReadOnlyList<AppRecord>>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var apps = await ExecuteQueuedRefreshAsync(cancellationToken);
            await databaseService.ReplaceAppsAsync(apps, cancellationToken);
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, apps);
        }
        catch (OperationCanceledException)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.Cancelled, "卸载器刷新已取消。");
        }
        catch (UnauthorizedAccessException exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.AccessDenied, exception.Message);
        }
        catch (Exception exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.Internal, exception.Message);
        }
    }

    private async Task<IReadOnlyList<AppRecord>> ExecuteQueuedRefreshAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<AppRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await taskQueue.EnqueueAsync(new TaskMeta(
            "uninstall-refresh",
            "软件清单全量刷新",
            false,
            TaskResource.Uninstaller), async workToken =>
        {
            try
            {
                var registryTask = Task.Run(() => registryEnumerator.Enumerate(), workToken);
                var storeTask = Task.Run(() => StoreAppEnumerator.Enumerate(), workToken);
                Task.WaitAll(new[] { registryTask, storeTask }, CancellationToken.None);
                var registryResult = registryTask.Result;
                var storeResult = storeTask.Result;
                if (!registryResult.IsSuccess && !storeResult.IsSuccess)
                {
                    throw new InvalidOperationException("All uninstall sources failed to enumerate.");
                }

                var sourceRecords = new List<AppRecord>();
                Append(sourceRecords, registryResult);
                Append(sourceRecords, storeResult);
                completion.SetResult(UninstallCatalogMerger.Merge(sourceRecords));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(workToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AppRecord>>> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return new Result<IReadOnlyList<AppRecord>>(
                ErrorType.None,
                string.Empty,
                await databaseService.ListAppsAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.Internal, exception.Message);
        }
    }

    private static void Append(List<AppRecord> records, Result<IReadOnlyList<AppRecord>> result)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            records.AddRange(result.Value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await taskQueue.DisposeAsync();
    }
}
