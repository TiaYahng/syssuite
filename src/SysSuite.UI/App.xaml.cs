using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SysSuite.Core;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;
using SysSuite.Core.Diagnostics;
using SysSuite.Core.Settings;
using SysSuite.Core.System;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI;

public partial class App : Application, IDisposable
{
    public IServiceProvider Services { get; private set; } = default!;
    private DiagnosticsService diagnostics = default!;
    private Mutex? instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        instanceMutex = new Mutex(true, @"Local\SysSuite.UI", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("SysSuite 已经在运行。", "SysSuite", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (!Environment.IsPrivilegedProcess && !TrySelfElevate())
        {
            Shutdown();
            return;
        }

        diagnostics = new DiagnosticsService();
        SerilogInitializer.Configure(diagnostics);
        Log.Information("SysSuite startup started ApplicationVersion={Version} Runtime={Runtime} IsElevated={IsElevated}",
            typeof(App).Assembly.GetName().Version,
            Environment.Version,
            Environment.IsPrivilegedProcess);

        var services = new ServiceCollection();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IDiagnosticsService>(diagnostics);
        services.AddSingleton<ISharedDatabaseService>(_ => new SharedDatabaseService(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysSuite", "syssuite.db")));
        services.AddSingleton<IUninstallEnumerationService, UninstallEnumerationService>();
        services.AddSingleton<IIconCacheService, IconCacheService>();
        services.AddSingleton<IUninstallService, UninstallService>();
        services.AddSingleton<ILeftoverScanner, LeftoverScanner>();
        services.AddSingleton<IForceDeleteService, ForceDeleteService>();
        services.AddSingleton<IAppChangeMonitor, AppChangeMonitor>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHardwareInfoService, WmiHardwareInfoService>();
        services.AddSingleton<IMonitorService, PerformanceMonitorService>();
        services.AddSingleton<ISensorService, LibreHardwareSensorService>();
        services.AddSingleton<ISmartService, SmartService>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IBenchmarkService, BenchmarkService>();
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.AddSingleton<IDiskInspectionService, DiskInspectionService>();
        Services = services.BuildServiceProvider();

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        new MainWindow().Show();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        CaptureAndShow(e.Exception);
    }

    /// <summary>
    /// 未提权时主动申请提权并重启自己。
    ///
    /// 正常情况下 manifest 的 <c>requireAdministrator</c> 会在进程启动前弹 UAC，
    /// 走不到这里。本方法覆盖两类例外：
    ///   1. 从无 manifest 的宿主（测试宿主、脚本、调试器）拉起本程序；
    ///   2. 用户或策略关闭了 UAC 自动提权行为。
    /// 返回 true 表示"新进程已拉起，本进程应立即退出"。
    /// </summary>
    private static bool TrySelfElevate()
    {
        var result = new ElevationService().RestartElevated();
        if (result.Requested)
        {
            return true;
        }

        var message = result.UserDeclined
            ? "SysSuite 需要管理员权限才能读取硬盘 SMART、清理系统盘等。\n\n你取消了提权请求，程序将退出。可右键程序图标选择「以管理员身份运行」重试。"
            : $"SysSuite 需要管理员权限运行。\n\n{result.FailureReason}\n\n请右键程序图标选择「以管理员身份运行」。";
        MessageBox.Show(message, "SysSuite 需要管理员权限", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CaptureAndShow(exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        CaptureAndShow(e.Exception);
    }

    private void CaptureAndShow(Exception exception)
    {
        diagnostics.CaptureCrashDump(exception);
        Log.Fatal(exception, "Unhandled exception");
        var crashWindow = new CrashWindow { ExceptionText = exception.ToString() };
        if (MainWindow is { IsLoaded: true, IsVisible: true })
        {
            crashWindow.Owner = MainWindow;
        }

        crashWindow.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
        Log.CloseAndFlush();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        instanceMutex = null;
    }
}
