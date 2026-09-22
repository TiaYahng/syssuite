using System.ComponentModel;
using System.Globalization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Abstractions.Settings;
using SysSuite.Core.System;

namespace SysSuite.Core.Data;

public sealed partial class ForceDeleteService : IForceDeleteService
{
    private readonly ISettingsService settingsService;
    private readonly string backupRootBase;

    public ForceDeleteService(ISettingsService settingsService, string? backupRootBase = null)
    {
        this.settingsService = settingsService;
        this.backupRootBase = backupRootBase ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public async Task<Result<ForceDeleteResult>> DeleteAsync(string path, string confirmationText, CancellationToken cancellationToken = default)
    {
        if (!settingsService.Current.EnableExperimentalFeatures || !settingsService.Current.EnableForceDelete)
        {
            return new Result<ForceDeleteResult>(ErrorType.AccessDenied, "强制删除默认关闭，必须同时启用实验能力和强制删除开关。");
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
        {
            return new Result<ForceDeleteResult>(ErrorType.NotFound, "目标路径不存在。");
        }

        var expectedConfirmation = CreateConfirmation(path);
        if (!string.Equals(confirmationText, expectedConfirmation, StringComparison.Ordinal))
        {
            return new Result<ForceDeleteResult>(ErrorType.InvalidInput, "确认句不匹配，操作已拒绝。");
        }

        var fullPath = Path.GetFullPath(path);
        if (ProtectedPaths.IsProtected(fullPath))
        {
            return new Result<ForceDeleteResult>(
                ErrorType.AccessDenied,
                $"目标位于系统保护路径（{ProtectedPaths.DescribeMatch(fullPath)}），强制删除已拒绝。");
        }

        var backupRoot = CreateBackupRoot(backupRootBase);
        try
        {
            Directory.CreateDirectory(backupRoot);
            await CreateBackupAsync(fullPath, backupRoot, cancellationToken);
            return await Task.Run(() =>
            {
                EnableTakeOwnershipPrivilege();
                return DeleteTree(fullPath, backupRoot);
            }, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new Result<ForceDeleteResult>(ErrorType.AccessDenied, exception.Message);
        }
        catch (OperationCanceledException)
        {
            return new Result<ForceDeleteResult>(ErrorType.Cancelled, "强制删除已取消。");
        }
        catch (Exception exception)
        {
            return new Result<ForceDeleteResult>(ErrorType.Internal, exception.Message);
        }
    }

    public static string CreateConfirmation(string path)
    {
        return $"强制删除 {Path.GetFullPath(path)}";
    }

    private static string CreateBackupRoot(string backupRootBase)
    {
        return Path.Combine(
            backupRootBase,
            "SysSuite",
            "force-delete-backups",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
    }

    private static async Task CreateBackupAsync(string fullPath, string backupRoot, CancellationToken cancellationToken)
    {
        if (File.Exists(fullPath))
        {
            await CopyFileAsync(new FileInfo(fullPath), Path.Combine(backupRoot, Path.GetFileName(fullPath)), cancellationToken);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(fullPath, "*", new EnumerationOptions
                 {
                     IgnoreInaccessible = true,
                     RecurseSubdirectories = true
                 }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(fullPath, file);
            var destination = Path.Combine(backupRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(new FileInfo(file), destination, cancellationToken);
        }
    }

    private static async Task CopyFileAsync(FileInfo source, string destination, CancellationToken cancellationToken)
    {
        await using var sourceStream = source.OpenRead();
        await using var destinationStream = File.Create(destination);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private static Result<ForceDeleteResult> DeleteTree(string fullPath, string backupRoot)
    {
        var deletedFiles = 0;
        var deletedDirectories = 0;
        var scheduledForReboot = 0;
        var errors = new List<string>();
        if (File.Exists(fullPath))
        {
            DeleteFileWithRecovery(fullPath, errors, ref deletedFiles, ref scheduledForReboot);
            return new Result<ForceDeleteResult>(ErrorType.None, string.Empty, new ForceDeleteResult(
                deletedFiles, 0, scheduledForReboot, errors, backupRoot));
        }

        foreach (var file in Directory.EnumerateFiles(fullPath, "*", new EnumerationOptions
                 {
                     IgnoreInaccessible = true,
                     RecurseSubdirectories = true
                 }))
        {
            ClearAttributes(file);
            DeleteFileWithRecovery(file, errors, ref deletedFiles, ref scheduledForReboot);
        }

        foreach (var directory in Directory.EnumerateDirectories(fullPath, "*", new EnumerationOptions
                 {
                     IgnoreInaccessible = true,
                     RecurseSubdirectories = true,
                     ReturnSpecialDirectories = false
                 })
                 .OrderByDescending(path => path.Length))
        {
            ClearAttributes(directory);
            try
            {
                Directory.Delete(directory, true);
                deletedDirectories++;
            }
            catch (Exception exception)
            {
                errors.Add($"{directory}: {exception.Message}");
            }
        }

        ClearAttributes(fullPath);
        try
        {
            Directory.Delete(fullPath, true);
            deletedDirectories++;
        }
        catch (Exception exception)
        {
            errors.Add($"{fullPath}: {exception.Message}");
        }

        return new Result<ForceDeleteResult>(ErrorType.None, string.Empty, new ForceDeleteResult(
            deletedFiles, deletedDirectories, scheduledForReboot, errors, backupRoot));
    }

    private static void DeleteFileWithRecovery(string path, List<string> errors, ref int deletedFiles, ref int scheduledForReboot)
    {
        try
        {
            File.Delete(path);
            deletedFiles++;
        }
        catch (IOException) when (MoveFileEx(path, null, MoveFileDelayUntilReboot))
        {
            scheduledForReboot++;
        }
        catch (Win32Exception) when (MoveFileEx(path, null, MoveFileDelayUntilReboot))
        {
            scheduledForReboot++;
        }
        catch (Exception exception)
        {
            errors.Add($"{path}: {exception.Message}");
        }
    }

    private static void ClearAttributes(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
        }
        catch (Exception)
        {
        }
    }

}
