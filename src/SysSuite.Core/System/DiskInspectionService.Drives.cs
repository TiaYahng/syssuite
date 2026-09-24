using SysSuite.Core.Abstractions.System;

// using 别名是文件级的，partial 之间不共享 —— 拆文件时必须把它带上
using IoDriveInfo = System.IO.DriveInfo;

namespace SysSuite.Core.System;

/// <summary>
/// 磁盘枚举相关的辅助（从主文件拆出，只为守住 300 行门禁）。
/// </summary>
public sealed partial class DiskInspectionService
{
    private static DriveOption CreateDriveOption(IoDriveInfo drive)
    {
        var totalBytes = 0L;
        var freeBytes = 0L;
        var isReady = drive.IsReady;
        if (isReady)
        {
            try
            {
                totalBytes = drive.TotalSize;
                freeBytes = drive.AvailableFreeSpace;
            }
            catch (IOException)
            {
                isReady = false;
            }
        }

        return new DriveOption(
            drive.Name,
            Path.GetFullPath(drive.Name),
            totalBytes,
            freeBytes,
            IsSystemDrive(drive.Name),
            isReady);
    }

    private static bool IsSystemDrive(string driveName)
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        var targetRoot = Path.GetPathRoot(driveName);
        return !string.IsNullOrEmpty(systemRoot)
            && !string.IsNullOrEmpty(targetRoot)
            && string.Equals(systemRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static List<DirectoryInfo> NormalizeRoots(IReadOnlyList<string> driveRoots)
    {
        return driveRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => new DirectoryInfo(root))
            .Where(root => root.Exists && !root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .ToList();
    }
}
