using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SysSuite.Core;
using SysSuite.Core.Abstractions;
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
            MessageBox.Show("SysSuite ?????", "SysSuite", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (!Environment.IsPrivilegedProcess)
        {
            MessageBox.Show("SysSuite ????????????????", "SysSuite", MessageBoxButton.OK, MessageBoxImage.Information);
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
        services.AddSingleton<INativeBridge, NativeBridge>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IHardwareInfoService, WmiHardwareInfoService>();
        services.AddSingleton<IMonitorService, PerformanceMonitorService>();
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
