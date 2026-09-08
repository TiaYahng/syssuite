using System.IO.Compression;
using System.IO;
using SysSuite.Core.Abstractions;

namespace SysSuite.Core.Diagnostics;

public sealed class DiagnosticsService : IDiagnosticsService
{
    public string RootDirectory { get; }

    public DiagnosticsService(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysSuite");
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(CrashDirectory);
    }

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string CrashDirectory => Path.Combine(RootDirectory, "crash");

    public Result CaptureCrashDump(Exception exception)
    {
        var fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmss}";
        var dumpResult = CrashDumpService.Write(Path.Combine(CrashDirectory, fileName + ".dmp"));
        File.WriteAllText(Path.Combine(CrashDirectory, fileName + ".txt"), exception.ToString());
        WriteCrashMetadata(fileName);
        return dumpResult;
    }

    public Result ExportLogs()
    {
        try
        {
            var outputPath = Path.Combine(RootDirectory, $"sys-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "*", SearchOption.AllDirectories))
            {
                archive.CreateEntryFromFile(file, Path.GetRelativePath(RootDirectory, file));
            }

            foreach (var file in Directory.EnumerateFiles(CrashDirectory, "*", SearchOption.AllDirectories))
            {
                archive.CreateEntryFromFile(file, Path.GetRelativePath(RootDirectory, file));
            }

            return Result.Success();
        }
        catch (IOException exception)
        {
            return Result.Failure(ErrorType.Internal, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure(ErrorType.AccessDenied, exception.Message);
        }
    }

    private void WriteCrashMetadata(string fileName)
    {
        try
        {
            var text = $"""
                Timestamp={DateTimeOffset.Now:O}
                ApplicationVersion={typeof(DiagnosticsService).Assembly.GetName().Version}
                RuntimeVersion={Environment.Version}
                OperatingSystem={Environment.OSVersion.VersionString}
                IsElevated={Environment.IsPrivilegedProcess}
                Is64BitProcess={Environment.Is64BitProcess}
                MachineName={Environment.MachineName}
                """;
            File.WriteAllText(Path.Combine(CrashDirectory, fileName + "-environment.txt"), text);

            var latestLog = Directory
                .EnumerateFiles(LogDirectory, "sys-*.log", SearchOption.TopDirectoryOnly)
                .Select(File.GetLastWriteTime)
                .Max();
            var latestLogPath = Directory
                .EnumerateFiles(LogDirectory, "sys-*.log", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => File.GetLastWriteTime(path) == latestLog);
            if (latestLogPath is not null)
            {
                var lines = File.ReadLines(latestLogPath).TakeLast(30);
                File.WriteAllLines(Path.Combine(CrashDirectory, fileName + "-logs.txt"), lines);
            }
        }
        catch (IOException)
        {
        }
    }
}
