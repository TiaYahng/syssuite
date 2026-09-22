using System.Diagnostics;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed partial class UninstallService
{
    private static async Task<Result> CreateBackupAsync(AppRecord app, string backupRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupRoot);
        try
        {
            if (!string.IsNullOrWhiteSpace(app.KeyPath) && app.KeyPath.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            {
                using var exportProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    ArgumentList =
                    {
                        "export",
                        app.KeyPath,
                        Path.Combine(backupRoot, "registry.reg"),
                        "/y"
                    },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (exportProcess is null)
                {
                    return Result.Failure(ErrorType.DependencyMissing, "注册表备份进程启动失败。");
                }

                await exportProcess.WaitForExitAsync(cancellationToken);
                if (exportProcess.ExitCode != 0)
                {
                    return Result.Failure(ErrorType.Internal, $"注册表备份失败（退出码 {exportProcess.ExitCode}）。");
                }
            }

            if (!string.IsNullOrWhiteSpace(app.InstallDir) && Directory.Exists(app.InstallDir))
            {
                var lines = Directory
                    .EnumerateFiles(app.InstallDir, "*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true
                    })
                    .Select(path =>
                    {
                        var file = new FileInfo(path);
                        return $"{path}|{file.Length}|{file.LastWriteTimeUtc:O}";
                    });
                await File.WriteAllLinesAsync(Path.Combine(backupRoot, "install-dir.manifest"), lines, cancellationToken);
            }

            return Result.Success();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure(ErrorType.AccessDenied, exception.Message);
        }
        catch (Exception exception)
        {
            return Result.Failure(ErrorType.Internal, exception.Message);
        }
    }

    private async Task SaveHistoryAsync(AppRecord app, int exitCode, string result)
    {
        var apps = await databaseService.ListAppsAsync();
        var current = apps.FirstOrDefault(candidate => candidate.StableKey == app.StableKey);
        await databaseService.SaveUpdateHistoryAsync(new UpdateHistoryRecord(
            0,
            current?.Id ?? 0,
            app.StableKey,
            app.Version,
            current?.Version,
            AppHistoryAction.Uninstall,
            $"{result}|ExitCode={exitCode}",
            DateTimeOffset.UtcNow));
    }
}
