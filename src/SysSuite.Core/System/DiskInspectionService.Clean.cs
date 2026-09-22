using System.Text.Json;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    private static readonly JsonSerializerOptions BackupJsonOptions = new() { WriteIndented = true };
    private sealed record BackupEntry(string OriginalPath, string BackupPath, string Category, long SizeBytes);

    public async Task<Result<CleanResult>> CleanAsync(
        DiskInspectionReport report,
        IEnumerable<CleanItem> items,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToArray();
        if (!ValidateReportItems(report, itemList))
        {
            return new Result<CleanResult>(ErrorType.InvalidInput, "包含不在本次检查结果中的路径。");
        }

        return await Task.Run(async () =>
        {
            var batchId = Guid.NewGuid().ToString("N");
            var batchRoot = Path.Combine(backupRoot, batchId);
            var manifestPath = Path.Combine(batchRoot, "manifest.json");
            var entries = new List<BackupEntry>();
            var deletedCount = 0;
            var failedCount = 0;
            var freedBytes = 0L;
            var errors = new List<string>();

            try
            {
                Directory.CreateDirectory(batchRoot);
                foreach (var item in itemList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!BackupItem(item, batchRoot, entries, errors))
                    {
                        failedCount++;
                        continue;
                    }

                    if (DeleteInspectionItem(item, errors, ref freedBytes))
                    {
                        deletedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                }

                await File.WriteAllTextAsync(
                    manifestPath,
                    JsonSerializer.Serialize(entries, BackupJsonOptions),
                    cancellationToken);

                var reversible = entries.Count > 0;
                if (databaseService is not null)
                {
                    await databaseService.SaveCleanHistoryAsync(new CleanHistoryRecord(
                        0,
                        batchId,
                        JsonSerializer.Serialize(entries),
                        freedBytes,
                        "backup-delete",
                        DateTimeOffset.UtcNow,
                        reversible,
                        reversible ? batchRoot : null,
                        reversible ? CleanRestoreState.Ready : CleanRestoreState.Expired,
                        reversible ? DateTimeOffset.UtcNow.AddDays(7) : null), cancellationToken);
                }

                if (!reversible && Directory.Exists(batchRoot))
                {
                    Directory.Delete(batchRoot, true);
                }

                return new Result<CleanResult>(ErrorType.None, string.Empty, new CleanResult(
                    deletedCount, failedCount, freedBytes, errors));
            }
            catch (OperationCanceledException)
            {
                return new Result<CleanResult>(ErrorType.Cancelled, "磁盘清理已取消。");
            }
            catch (IOException exception)
            {
                return new Result<CleanResult>(ErrorType.Internal, exception.Message);
            }
        }, cancellationToken);
    }

    public async Task<Result<CleanResult>> RestoreLatestAsync(CancellationToken cancellationToken = default)
    {
        if (databaseService is null)
        {
            return new Result<CleanResult>(ErrorType.InvalidInput, "当前清理服务未连接历史数据库。");
        }

        var history = await databaseService.ListCleanHistoryAsync(20, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var latest = history.FirstOrDefault(item =>
            item.Reversible
            && (item.RestoreState is CleanRestoreState.Ready or CleanRestoreState.Partial)
            && (item.ExpiresAt is null || item.ExpiresAt > now)
            && !string.IsNullOrWhiteSpace(item.BackupRoot));
        if (latest is null || latest.BackupRoot is null)
        {
            return new Result<CleanResult>(ErrorType.InvalidInput, "没有可撤销的最近一次清理。");
        }

        var manifestPath = Path.Combine(latest.BackupRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new Result<CleanResult>(ErrorType.Internal, "清理备份清单不存在，无法撤销。");
        }

        var entries = JsonSerializer.Deserialize<List<BackupEntry>>(await File.ReadAllTextAsync(manifestPath, cancellationToken)) ?? [];
        var restored = 0;
        var failed = 0;
        var bytes = 0L;
        var errors = new List<string>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(entry.BackupPath))
                {
                    continue;
                }

                var destination = entry.OriginalPath;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    failed++;
                    AddError(errors, destination, "目标文件已存在，为避免覆盖未执行恢复。");
                    continue;
                }

                File.Copy(entry.BackupPath, destination, false);
                restored++;
                bytes += entry.SizeBytes;
            }
            catch (IOException exception)
            {
                failed++;
                AddError(errors, entry.OriginalPath, exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                failed++;
                AddError(errors, entry.OriginalPath, exception.Message);
            }
        }

        if (restored > 0 && failed == 0)
        {
            TryDeleteDirectory(latest.BackupRoot);
            await databaseService.UpdateCleanHistoryStateAsync(latest.Id, CleanRestoreState.Restored, cancellationToken);
        }
        else if (restored > 0)
        {
            await databaseService.UpdateCleanHistoryStateAsync(latest.Id, CleanRestoreState.Partial, cancellationToken);
        }

        return new Result<CleanResult>(ErrorType.None, string.Empty, new CleanResult(restored, failed, bytes, errors));
    }

    private static bool BackupItem(CleanItem item, string batchRoot, List<BackupEntry> entries, List<string> errors)
    {
        if (item.Category == "空文件夹")
        {
            return true;
        }

        try
        {
            var source = new FileInfo(item.Path);
            if (!source.Exists)
            {
                return true;
            }

            var safeName = Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(
                global::System.Text.Encoding.UTF8.GetBytes(source.FullName))).ToLowerInvariant();
            var backupPath = Path.Combine(batchRoot, safeName + source.Extension);
            File.Copy(source.FullName, backupPath, false);
            entries.Add(new BackupEntry(source.FullName, backupPath, item.Category, source.Length));
            return true;
        }
        catch (IOException exception)
        {
            AddError(errors, item.Path, $"备份失败：{exception.Message}");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            AddError(errors, item.Path, $"备份失败：{exception.Message}");
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool ValidateReportItems(DiskInspectionReport report, IReadOnlyList<CleanItem> items)
    {
        var reportPaths = report.Items
            .Select(item => GetFullPath(item.Path))
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items.All(item => GetFullPath(item.Path) is { } path && reportPaths.Contains(path));
    }

    private static bool DeleteInspectionItem(CleanItem item, List<string> errors, ref long freedBytes)
    {
        // G7：删除前的最后一道硬拒绝，规则引擎即使误命中也不允许穿透
        if (ProtectedPaths.IsProtected(item.Path))
        {
            return AddError(errors, item.Path, $"位于系统保护路径（{ProtectedPaths.DescribeMatch(item.Path)}），已拒绝删除。");
        }

        try
        {
            if (item.Category is "重复文件" or "临时文件" or "大文件")
            {
                return DeleteDuplicateFile(item, ref freedBytes);
            }

            return item.Category == "空文件夹" && DeleteEmptyFolder(item, errors);
        }
        catch (IOException exception)
        {
            return AddError(errors, item.Path, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return AddError(errors, item.Path, exception.Message);
        }
    }

    private static bool DeleteDuplicateFile(CleanItem item, ref long freedBytes)
    {
        var file = new FileInfo(item.Path);
        if (!file.Exists)
        {
            return true;
        }

        var size = file.Length;
        file.Delete();
        freedBytes += size;
        return true;
    }

    private static bool DeleteEmptyFolder(CleanItem item, List<string> errors)
    {
        var directory = new DirectoryInfo(item.Path);
        if (!directory.Exists || IsApplicationDirectory(directory, out _))
        {
            return AddError(errors, item.Path, "目录已变化或属于应用目录。");
        }

        if (!IsEmptyDirectory(directory))
        {
            return AddError(errors, item.Path, "目录已不再为空。");
        }

        directory.Delete(false);
        return true;
    }

    private static bool AddError(List<string> errors, string path, string message)
    {
        if (errors.Count < MaximumErrorMessages)
        {
            errors.Add($"{path}: {message}");
        }

        return false;
    }
}