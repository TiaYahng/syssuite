using Serilog;
using Serilog.Events;
using System.Globalization;

namespace SysSuite.Core.Diagnostics;

public static class SerilogInitializer
{
    public static ILogger Configure(DiagnosticsService diagnostics)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "SysSuite")
            .Enrich.WithProperty("ApplicationVersion", typeof(DiagnosticsService).Assembly.GetName().Version?.ToString() ?? "unknown")
            .Enrich.WithProperty("RuntimeVersion", Environment.Version)
            .Enrich.WithProperty("IsElevated", Environment.IsPrivilegedProcess)
            .WriteTo.File(
                Path.Combine(diagnostics.LogDirectory, "sys-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
        return Log.Logger;
    }
}
