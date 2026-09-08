namespace SysSuite.Core.Abstractions;

public interface IDiagnosticsService
{
    string RootDirectory { get; }

    string LogDirectory { get; }

    string CrashDirectory { get; }

    Result CaptureCrashDump(Exception exception);

    Result ExportLogs();
}
