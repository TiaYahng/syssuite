using System.Diagnostics;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class UninstallService : IUninstallService, IDisposable
{
    private const int InteractiveTimeoutSeconds = 600;
    private readonly ISharedDatabaseService databaseService;
    private readonly IUninstallEnumerationService enumerationService;
    private readonly ILeftoverScanner leftoverScanner;
    private readonly SemaphoreSlim executionLock = new(1, 1);

    public UninstallService(
        ISharedDatabaseService databaseService,
        IUninstallEnumerationService enumerationService,
        ILeftoverScanner leftoverScanner)
    {
        this.databaseService = databaseService;
        this.enumerationService = enumerationService;
        this.leftoverScanner = leftoverScanner;
    }

    public async Task<Result<UninstallResult>> UninstallAsync(AppRecord app, UninstallMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        await executionLock.WaitAsync(cancellationToken);
        try
        {
            var batchId = $"uninstall-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            var backupRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SysSuite",
                "backups",
                batchId);
            var backupResult = await CreateBackupAsync(app, backupRoot, cancellationToken);
            if (!backupResult.IsSuccess)
            {
                return new Result<UninstallResult>(backupResult.Error, backupResult.Message);
            }

            var startInfo = CreateStartInfo(app, mode);
            if (startInfo is null)
            {
                return new Result<UninstallResult>(ErrorType.InvalidInput, "未找到可执行的卸载命令。");
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new Result<UninstallResult>(ErrorType.DependencyMissing, "卸载进程启动失败。");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(InteractiveTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await SaveHistoryAsync(app, -1, "TimedOut");
                await enumerationService.RefreshAsync(cancellationToken);
                return TimedOutResult(batchId, backupRoot);
            }

            var outcome = process.ExitCode switch
            {
                0 => UninstallOutcome.Succeeded,
                3010 => UninstallOutcome.RebootRequired,
                1641 => UninstallOutcome.RebootRequired,
                _ => UninstallOutcome.Failed
            };
            await SaveHistoryAsync(app, process.ExitCode, outcome.ToString());
            await enumerationService.RefreshAsync(cancellationToken);
            IReadOnlyList<LeftoverItem>? leftovers = null;
            if (outcome is UninstallOutcome.Succeeded or UninstallOutcome.RebootRequired)
            {
                var leftoverResult = await leftoverScanner.ScanAsync(app, cancellationToken);
                if (leftoverResult.IsSuccess)
                {
                    leftovers = leftoverResult.Value;
                }
            }

            return new Result<UninstallResult>(ErrorType.None, string.Empty, new UninstallResult(
                batchId,
                outcome,
                process.ExitCode,
                outcome is UninstallOutcome.Failed ? $"卸载程序返回退出码 {process.ExitCode}。" : string.Empty,
                backupRoot,
                leftovers));
        }
        catch (OperationCanceledException)
        {
            return new Result<UninstallResult>(ErrorType.Cancelled, "卸载已取消。");
        }
        catch (Exception exception)
        {
            return new Result<UninstallResult>(ErrorType.Internal, exception.Message);
        }
        finally
        {
            executionLock.Release();
        }
    }

    private static Result<UninstallResult> TimedOutResult(string batchId, string backupRoot)
    {
        return new Result<UninstallResult>(ErrorType.None, string.Empty, new UninstallResult(
            batchId,
            UninstallOutcome.TimedOut,
            -1,
            "卸载超过 600 秒，进程仍在运行，请选择继续等待或手动结束。",
            backupRoot));
    }

    private static ProcessStartInfo? CreateStartInfo(AppRecord app, UninstallMode mode)
    {
        if (app.Source == AppSource.Msi)
        {
            var productCode = ExtractProductCode(app);
            if (productCode is not null)
            {
                return Create("msiexec.exe", $"/x {productCode} /qn");
            }
        }

        if (app.Source == AppSource.Store)
        {
            var package = app.StableKey.StartsWith("STORE|", StringComparison.Ordinal)
                ? app.StableKey["STORE|".Length..]
                : app.Hash;
            if (string.IsNullOrWhiteSpace(package))
            {
                return null;
            }

            var escapedPackage = package.Replace("'", "''", StringComparison.Ordinal);
            return Create("powershell.exe", $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{escapedPackage}'\"");
        }

        var command = mode == UninstallMode.Quiet ? app.QuietString ?? app.UninstallString : app.UninstallString;
        return ParseCommand(command);
    }

    private static string? ExtractProductCode(AppRecord app)
    {
        if (app.KeyPath is not null)
        {
            var start = app.KeyPath.IndexOf('{');
            var end = start >= 0 ? app.KeyPath.IndexOf('}', start) : -1;
            if (start >= 0 && end > start)
            {
                return app.KeyPath[start..(end + 1)];
            }
        }

        var match = global::System.Text.RegularExpressions.Regex.Match(
            app.StableKey,
            @"\{[0-9A-Fa-f\-]{36}\}",
            global::System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }

    private static ProcessStartInfo Create(string fileName, string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(fileName),
            Arguments = Environment.ExpandEnvironmentVariables(arguments),
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static ProcessStartInfo? ParseCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var value = Environment.ExpandEnvironmentVariables(command.Trim());
        string fileName;
        string arguments;
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end < 0)
            {
                return null;
            }

            fileName = value[1..end];
            arguments = value[(end + 1)..].TrimStart();
        }
        else
        {
            var separator = value.IndexOf(' ');
            if (separator < 0)
            {
                fileName = value;
                arguments = string.Empty;
            }
            else
            {
                fileName = value[..separator];
                arguments = value[(separator + 1)..].TrimStart();
            }
        }

        if (fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            return Create("msiexec.exe", $"/x \"{fileName}\" /qn");
        }

        if (fileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            arguments = arguments.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
        }

        return Create(fileName, arguments);
    }

    public void Dispose()
    {
        executionLock.Dispose();
    }
}
