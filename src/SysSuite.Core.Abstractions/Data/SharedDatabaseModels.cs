namespace SysSuite.Core.Abstractions.Data;

public enum AppSource
{
    Registry,
    Msi,
    Store
}

public enum CleanRestoreState
{
    Pending,
    Ready,
    Partial,
    Restored,
    Expired
}

public enum AppHistoryAction
{
    Install,
    Upgrade,
    Uninstall
}

public sealed record AppRecord(
    long Id,
    string StableKey,
    string Name,
    string? Publisher,
    string? Version,
    string? InstallDate,
    long? Size,
    string? UninstallString,
    string? QuietString,
    AppSource Source,
    string? KeyPath,
    string? InstallDir,
    string? IconPath,
    string? Hash,
    DateTimeOffset UpdatedAt)
{
    public static AppRecord Create(
        string stableKey,
        string name,
        AppSource source,
        string? publisher = null,
        string? version = null,
        string? installDate = null,
        long? size = null,
        string? uninstallString = null,
        string? quietString = null,
        string? keyPath = null,
        string? installDir = null,
        string? iconPath = null,
        string? hash = null)
    {
        return new AppRecord(
            0,
            stableKey,
            name,
            publisher,
            version,
            installDate,
            size,
            uninstallString,
            quietString,
            source,
            keyPath,
            installDir,
            iconPath,
            hash,
            DateTimeOffset.UtcNow);
    }
}

public sealed record CleanHistoryRecord(
    long Id,
    string BatchId,
    string ItemsJson,
    long FreedBytes,
    string Mode,
    DateTimeOffset At,
    bool Reversible,
    string? BackupRoot,
    CleanRestoreState RestoreState,
    DateTimeOffset? ExpiresAt);

public sealed record UpdateHistoryRecord(
    long Id,
    long AppId,
    string AppStableKey,
    string? FromVersion,
    string? ToVersion,
    AppHistoryAction Action,
    string Result,
    DateTimeOffset At);

public sealed record SecurityAuditRecord(
    long Id,
    string Action,
    string OldState,
    string NewState,
    DateTimeOffset? AutoRestoreAt,
    DateTimeOffset At,
    string RequestedBy,
    int ProcessId,
    string CommandResult,
    string RestoreResult);

public interface ISharedDatabaseService
{
    Task<IReadOnlyList<AppRecord>> ListAppsAsync(CancellationToken cancellationToken = default);
    Task ReplaceAppsAsync(IReadOnlyCollection<AppRecord> apps, CancellationToken cancellationToken = default);
    Task SaveCleanHistoryAsync(CleanHistoryRecord record, CancellationToken cancellationToken = default);
    Task UpdateCleanHistoryStateAsync(long id, CleanRestoreState state, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanHistoryRecord>> ListCleanHistoryAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task SaveUpdateHistoryAsync(UpdateHistoryRecord record, CancellationToken cancellationToken = default);
    Task SaveSecurityAuditAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default);
}

public interface IUninstallEnumerationService
{
    Task<Result<IReadOnlyList<AppRecord>>> RefreshAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<AppRecord>>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IIconCacheService
{
    Task<Result<string?>> GetIconPathAsync(AppRecord app, CancellationToken cancellationToken = default);
}

public enum UninstallMode
{
    Quiet,
    Interactive
}

public enum UninstallOutcome
{
    Succeeded,
    RebootRequired,
    TimedOut,
    Failed,
    Cancelled
}

public sealed record UninstallResult(
    string BatchId,
    UninstallOutcome Outcome,
    int ExitCode,
    string Message,
    string? BackupRoot,
    IReadOnlyList<LeftoverItem>? Leftovers = null);

public interface IUninstallService
{
    Task<Result<UninstallResult>> UninstallAsync(AppRecord app, UninstallMode mode, CancellationToken cancellationToken = default);
}

public enum LeftoverKind
{
    InstallDirectory,
    AppDataDirectory,
    RegistryKey,
    RegistryValue
}

public sealed record LeftoverItem(
    string Path,
    LeftoverKind Kind,
    string Reason,
    bool Selected = true);

public interface ILeftoverScanner
{
    Task<Result<IReadOnlyList<LeftoverItem>>> ScanAsync(AppRecord app, CancellationToken cancellationToken = default);
    Task<Result<int>> DeleteAsync(IReadOnlyCollection<LeftoverItem> items, CancellationToken cancellationToken = default);
}

public sealed record ForceDeleteResult(
    int DeletedFiles,
    int DeletedDirectories,
    int ScheduledForReboot,
    IReadOnlyList<string> Errors,
    string BackupRoot);

public interface IForceDeleteService
{
    Task<Result<ForceDeleteResult>> DeleteAsync(string path, string confirmationText, CancellationToken cancellationToken = default);
}

public interface IAppChangeMonitor : IDisposable
{
    event EventHandler<IReadOnlyList<AppRecord>>? AppListChanged;

    bool IsRunning { get; }

    void Start();

    void StopMonitoring();
}
