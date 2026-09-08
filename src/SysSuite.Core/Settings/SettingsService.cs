using System.IO;
using System.Text.Json;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Settings;

namespace SysSuite.Core.Settings;

public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = AppSettingsJsonContext.Default
    };

    private readonly string filePath;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly object debounceGate = new();
    private CancellationTokenSource? debounceCancellation;
    private Task saveTask = Task.FromResult(Result.Success());

    public SettingsService(string? settingsDirectory = null)
    {
        settingsDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SysSuite");
        Directory.CreateDirectory(settingsDirectory);
        filePath = Path.Combine(settingsDirectory, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            var tempPath = filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, filePath, true);
                    return Result.Success();
                }
                catch (IOException) when (attempt <= 3)
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (IOException exception)
                {
                    return Result.Failure(ErrorType.Internal, exception.Message);
                }
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure(ErrorType.AccessDenied, exception.Message);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public void SaveDebounced()
    {
        lock (debounceGate)
        {
            debounceCancellation?.Cancel();
            debounceCancellation?.Dispose();
            debounceCancellation = new CancellationTokenSource();
            var cancellation = debounceCancellation.Token;
            saveTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, cancellation);
                    return await SaveAsync(cancellation);
                }
                catch (OperationCanceledException)
                {
                    return Result.Success();
                }
                catch (ObjectDisposedException)
                {
                    return Result.Success();
                }
            });
        }
    }

    public void Dispose()
    {
        lock (debounceGate)
        {
            debounceCancellation?.Cancel();
            debounceCancellation?.Dispose();
            debounceCancellation = null;
        }

        try
        {
            saveTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        saveLock.Dispose();
    }

    private AppSettings Load()
    {
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
