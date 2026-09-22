using System.Globalization;
using System.IO;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed class TempCleanerService : ICleanerService
{
    private const int MaximumErrorMessages = 20;
    private static readonly TimeSpan MinimumFileAge = TimeSpan.FromHours(24);
    private static readonly EnumerationOptions ScanOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false
    };
    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".reg", ".config", ".xml", ".json", ".ini", ".dmp"
    };

    private static readonly HashSet<string> CautionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".log", ".zip", ".7z", ".rar",
        ".jpg", ".jpeg", ".png", ".gif", ".mp3", ".mp4", ".mov", ".bak", ".old"
    };

    private readonly string userTempRoot;
    private readonly string windowsTempRoot;

    public TempCleanerService(string? userTempRoot = null, string? windowsTempRoot = null)
    {
        this.userTempRoot = userTempRoot ?? Path.GetTempPath();
        this.windowsTempRoot = windowsTempRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Temp");
    }

    public async Task<Result<IReadOnlyList<CleanItem>>> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var items = new List<CleanItem>();
                ScanRoot(userTempRoot, "用户临时文件", items, cancellationToken);
                ScanRoot(windowsTempRoot, "系统临时文件", items, cancellationToken);
                return new Result<IReadOnlyList<CleanItem>>(ErrorType.None, string.Empty, items);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new Result<IReadOnlyList<CleanItem>>(ErrorType.Cancelled, "扫描已取消。");
        }
        catch (UnauthorizedAccessException exception)
        {
            return new Result<IReadOnlyList<CleanItem>>(ErrorType.AccessDenied, exception.Message);
        }
    }

    public async Task<Result<CleanResult>> CleanAsync(IEnumerable<CleanItem> items, CancellationToken cancellationToken = default)
    {
        var itemList = items.ToArray();
        var safeRoots = GetSafeRoots();
        if (itemList.Any(item => !safeRoots.Any(root => IsWithinRoot(item.Path, root))))
        {
            return new Result<CleanResult>(ErrorType.InvalidInput, "包含超出安全清理范围的路径。");
        }

        try
        {
            return await Task.Run(() =>
            {
                var deletedCount = 0;
                var failedCount = 0;
                var freedBytes = 0L;
                var errors = new List<string>();

                foreach (var item in itemList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DeleteFile(item, errors, ref freedBytes))
                    {
                        deletedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                }

                return new Result<CleanResult>(ErrorType.None, string.Empty, new CleanResult(
                    deletedCount,
                    failedCount,
                    freedBytes,
                    errors));
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new Result<CleanResult>(ErrorType.Cancelled, "清理已取消。");
        }
    }

    private IReadOnlyList<string> GetSafeRoots()
    {
        return
        [
            Path.GetFullPath(userTempRoot),
            Path.GetFullPath(windowsTempRoot)
        ];
    }

    private static bool IsWithinRoot(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static void ScanRoot(string root, string category, List<CleanItem> items, CancellationToken cancellationToken)
    {
        var rootDirectory = new DirectoryInfo(root);
        if (!rootDirectory.Exists || rootDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        foreach (var file in rootDirectory.EnumerateFiles("*", ScanOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCleanableTempFile(file))
            {
                continue;
            }

            var (risk, importance) = AnalyzeFile(file);
            items.Add(new CleanItem(file.FullName, category, file.Length, risk, importance));
        }
    }

    internal static bool IsCleanableTempFile(FileInfo file)
    {
        try
        {
            return file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                ? false
                : file.LastWriteTimeUtc <= DateTime.UtcNow - MinimumFileAge;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
            }
    }

    internal static (CleanRisk Risk, string Importance) AnalyzeFile(FileInfo file)
    {
        var age = DateTime.UtcNow - file.LastWriteTimeUtc;
        if (RiskyExtensions.Contains(file.Extension)
            || file.Name.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || file.Name.Contains("install", StringComparison.OrdinalIgnoreCase)
            || file.Name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return (CleanRisk.Risky, "程序、安装包或配置相关内容，默认不清理。");
        }

        if (age <= TimeSpan.FromDays(7) || CautionExtensions.Contains(file.Extension))
        {
            return (CleanRisk.Caution, "近期文件、文档、媒体、压缩包或日志，建议先确认。");
        }

        return (CleanRisk.Safe, "超过 7 天的常规临时缓存。");
    }

    private static bool DeleteFile(CleanItem item, List<string> errors, ref long freedBytes)
    {
        try
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
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException exception)
        {
            AddError(errors, item.Path, exception.Message);
            return false;
        }
        catch (IOException exception)
        {
            AddError(errors, item.Path, exception.Message);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            AddError(errors, item.Path, exception.Message);
            return false;
        }
    }

    private static void AddError(List<string> errors, string path, string message)
    {
        if (errors.Count >= MaximumErrorMessages)
        {
            return;
        }

        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: {message}"));
    }
}
