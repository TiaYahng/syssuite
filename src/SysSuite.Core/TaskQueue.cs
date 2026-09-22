using System.Threading.Channels;
using SysSuite.Core.Abstractions;

namespace SysSuite.Core;

public enum TaskResource
{
    None,
    DiskDrive,
    Registry,
    Desktop,
    Uninstaller
}

public sealed record TaskMeta(string Id, string Title, bool IsCritical = false, params TaskResource[] ExclusiveResources);

public sealed record TaskExecution(TaskMeta Meta, Func<CancellationToken, Task> Work);

public sealed class TaskCoordinator : IAsyncDisposable
{
    private readonly Channel<TaskExecution> queue = Channel.CreateUnbounded<TaskExecution>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly SemaphoreSlim executorLock = new(1, 1);
    private readonly HashSet<TaskResource> heldResources = [];
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private bool disposed;

    public TaskCoordinator()
    {
        worker = RunAsync(cancellation.Token);
    }

    public async Task<Result> EnqueueAsync(TaskMeta meta, Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(work);
        if (disposed)
        {
        return Result.Failure(ErrorType.Internal, "TaskCoordinator has been disposed.");
        }

        await queue.Writer.WriteAsync(new TaskExecution(meta, work), cancellation.Token);
        return Result.Success();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var execution in queue.Reader.ReadAllAsync(cancellationToken))
            {
                await executorLock.WaitAsync(cancellationToken);
                try
                {
                    if (execution.Meta.ExclusiveResources.Length == 0 || execution.Meta.ExclusiveResources.All(resource => heldResources.Add(resource)))
                    {
                        try
                        {
                            await execution.Work(cancellationToken);
                        }
                        finally
                        {
                            foreach (var resource in execution.Meta.ExclusiveResources)
                            {
                                heldResources.Remove(resource);
                            }
                        }
                    }
                }
                finally
                {
                    executorLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        queue.Writer.TryComplete();
        try
        {
            await worker;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await cancellation.CancelAsync();
            cancellation.Dispose();
            executorLock.Dispose();
        }
    }
}
