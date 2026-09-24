using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Collections.Concurrent;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.Abstractions.Data;
using IoDriveInfo = System.IO.DriveInfo;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService : IDiskInspectionService, IDisposable
{
    private const long MinimumDuplicateSize = 1024 * 1024;
    private readonly ISharedDatabaseService? databaseService;
    private readonly string backupRoot;
    private readonly CleanRuleProvider ruleProvider;
    private bool disposed;

    public DiskInspectionService(ISharedDatabaseService? databaseService = null)
    {
        this.databaseService = databaseService;
        backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysSuite", "clean-backups");
        ruleProvider = new CleanRuleProvider(Path.Combine(AppContext.BaseDirectory, "rules", "clean-temp.json"));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ruleProvider.Dispose();
        GC.SuppressFinalize(this);
    }
    private const int PartialHashLength = 64 * 1024;
    private const int MaximumErrorMessages = 20;

    private static readonly EnumerationOptions ScanOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    private static readonly EnumerationOptions EmptyOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    private static readonly HashSet<string> ApplicationDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "program files", "program files (x86)", "programdata", "appdata", "applications",
        "portableapps", "portable", "apps", "program", "programs", "software", "software distribution",
        "tools", "软件", "程序", "应用", "应用程序", "应用软件", "便携软件", "绿色软件", "工具", "software"
    };

    private static readonly HashSet<string> ApplicationFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi", ".msix", ".appx", ".jar"
    };

    private static readonly HashSet<string> ApplicationMarkerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "appinfo.ini", "portable.dat", "portable.ini", "launcher.ini"
    };

    private static readonly HashSet<string> ApplicationRelatedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "my mastercam", "shared my mastercam"
    };

    public async Task<Result<IReadOnlyList<DriveOption>>> GetDrivesAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var drives = IoDriveInfo.GetDrives()
                    .Where(drive => drive.DriveType == global::System.IO.DriveType.Fixed)
                    .Select(CreateDriveOption)
                    .OrderByDescending(drive => drive.IsSystem)
                    .ThenBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new Result<IReadOnlyList<DriveOption>>(ErrorType.None, string.Empty, drives);
            }
            catch (OperationCanceledException)
            {
                return new Result<IReadOnlyList<DriveOption>>(ErrorType.Cancelled, "读取磁盘列表已取消。");
            }
            catch (IOException exception)
            {
                return new Result<IReadOnlyList<DriveOption>>(ErrorType.Internal, exception.Message);
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public async Task<Result<DiskInspectionReport>> ScanAsync(
        IReadOnlyList<string> driveRoots,
        IProgress<DiskScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Factory.StartNew(() =>
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                cancellationToken.ThrowIfCancellationRequested();
                var roots = NormalizeRoots(driveRoots);
                if (roots.Count == 0)
                {
                    return new Result<DiskInspectionReport>(ErrorType.InvalidInput, "请选择要检查的固定磁盘。");
                }

                var tracker = new ScanTracker(progress);
                var concurrentFiles = new ConcurrentBag<FileInfo>();
                var concurrentItems = new ConcurrentBag<CleanItem>();
                roots.AsParallel()
                    .WithDegreeOfParallelism(Math.Min(roots.Count, DuplicateScanParallelism))
                    .WithCancellation(cancellationToken)
                    .ForAll(root => InspectRoot(root, concurrentFiles, concurrentItems, tracker, cancellationToken));
                var files = concurrentFiles.ToList();
                var items = concurrentItems.ToList();
                AddLargeFileItems(files, items, cancellationToken);
                FindTempItems(roots, items, tracker, cancellationToken);
                FindRecycleBinItems(roots, items, tracker);
                var duplicateCandidates = files
                    .GroupBy(file => file.Length)
                    .Where(group => group.Count() > 1)
                    .SelectMany(group => group)
                    .ToArray();
                var duplicateWorkBytes = duplicateCandidates
                    .Sum(file => Math.Min(file.Length, PartialHashLength) + file.Length);
                tracker.BeginPhase("正在识别重复文件", duplicateCandidates.Length, duplicateWorkBytes);
                items.AddRange(FindDuplicateItems(duplicateCandidates, tracker, cancellationToken));
                tracker.CompletePhase("扫描完成");
                return new Result<DiskInspectionReport>(ErrorType.None, string.Empty, CreateReport(roots, items));
            }
            catch (OperationCanceledException)
            {
                return new Result<DiskInspectionReport>(ErrorType.Cancelled, "磁盘检查已取消。");
            }
            catch (IOException exception)
            {
                return new Result<DiskInspectionReport>(ErrorType.Internal, exception.Message);
            }
            finally
            {
                Thread.CurrentThread.Priority = ThreadPriority.Normal;
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void InspectRoot(
        DirectoryInfo root,
        ConcurrentBag<FileInfo> files,
        ConcurrentBag<CleanItem> items,
        ScanTracker tracker,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            tracker.ReportPhase("正在检查目录结构", directory.FullName);
            if (IsApplicationDirectory(directory, out var children))
            {
                continue;
            }

            if (!ReferenceEquals(directory, root)
                && children.Directories.Count == 0
                && children.Files.Count == 0
                && IsEmptyDirectory(directory))
            {
                items.Add(new CleanItem(
                    directory.FullName,
                    "空文件夹",
                    0,
                    CleanRisk.Safe,
                    "当前没有任何文件或子文件夹。"));
            }

            var duplicateCandidates = children.Files.Where(file => file.Length >= MinimumDuplicateSize).ToArray();
            foreach (var file in duplicateCandidates)
            {
                files.Add(file);
            }

            foreach (var file in duplicateCandidates)
            {
                tracker.ReportFile(file.FullName, file.Length);
            }

            foreach (var child in children.Directories)
            {
                pending.Push(child);
            }
        }
    }

    private static bool IsEmptyDirectory(DirectoryInfo directory)
    {
        try
        {
            return !directory.EnumerateFileSystemInfos("*", EmptyOptions).Any();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string? GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record DirectoryChildren(
        IReadOnlyList<DirectoryInfo> Directories,
        IReadOnlyList<FileInfo> Files);
}
