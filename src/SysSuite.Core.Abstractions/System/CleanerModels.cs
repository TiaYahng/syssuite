namespace SysSuite.Core.Abstractions.System;

public enum CleanRisk
{
    Safe,
    Caution,
    Risky
}

public record CleanItem(
    string Path,
    string Category,
    long SizeBytes,
    CleanRisk Risk,
    string Importance);

public sealed record CleanResult(
    int DeletedCount,
    int FailedCount,
    long FreedBytes,
    IReadOnlyList<string> Errors,
    string RestorePointMessage = "");

public interface ICleanerService
{
    Task<Result<IReadOnlyList<CleanItem>>> ScanAsync(CancellationToken cancellationToken = default);

    Task<Result<CleanResult>> CleanAsync(IEnumerable<CleanItem> items, CancellationToken cancellationToken = default);
}

public sealed record DuplicateFile(
    string Path,
    long SizeBytes,
    DateTime LastWriteTimeUtc);

public sealed record DuplicateGroup(
    string Hash,
    long SizeBytes,
    string KeepPath,
    IReadOnlyList<DuplicateFile> Files);

public interface IDuplicateFileService
{
    Task<Result<IReadOnlyList<DuplicateGroup>>> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed record DriveOption(
    string Name,
    string RootPath,
    long TotalBytes,
    long FreeBytes,
    bool IsSystem,
    bool IsReady);

public sealed record DiskInspectionReport(
    IReadOnlyList<string> Roots,
    IReadOnlyList<DiskCleanItem> Items,
    IReadOnlyList<DiskGroup> Groups);

public sealed record DiskGroup(
    string Key,
    string Title,
    string Category,
    long SizeBytes,
    int Count,
    string Summary);

public sealed record DiskCleanItem(
    string Path,
    string Category,
    long SizeBytes,
    CleanRisk Risk,
    string Importance,
    DiskGroup Group)
    : CleanItem(Path, Category, SizeBytes, Risk, Importance);

public sealed record DiskScanProgress(
    string Phase,
    double? Progress,
    long ProcessedFiles,
    long TotalFiles,
    long ProcessedBytes,
    long TotalBytes,
    string CurrentPath,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining);

public interface IDiskInspectionService
{
    Task<Result<IReadOnlyList<DriveOption>>> GetDrivesAsync(CancellationToken cancellationToken = default);

    Task<Result<DiskInspectionReport>> ScanAsync(
        IReadOnlyList<string> driveRoots,
        IProgress<DiskScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <param name="createRestorePoint">
    /// 清理前是否创建系统还原点（T3.4）。创建失败只降级为告警，不影响清理执行。
    /// </param>
    Task<Result<CleanResult>> CleanAsync(
        DiskInspectionReport report,
        IEnumerable<CleanItem> items,
        bool createRestorePoint = false,
        CancellationToken cancellationToken = default);

    Task<Result<CleanResult>> RestoreLatestAsync(CancellationToken cancellationToken = default);
}
