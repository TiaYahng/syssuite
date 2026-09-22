using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;

namespace SysSuite.UI;

public partial class CrashWindow : Window
{
    public string ExceptionText
    {
        get => ExceptionTextBox.Text;
        set => ExceptionTextBox.Text = value;
    }

    public CrashWindow()
    {
        InitializeComponent();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) =>
        OpenDirectory(((App)Application.Current).Services.GetRequiredService<IDiagnosticsService>().LogDirectory);

    private void OnOpenCrashDirectory(object sender, RoutedEventArgs e) =>
        OpenDirectory(((App)Application.Current).Services.GetRequiredService<IDiagnosticsService>().CrashDirectory);

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var diagnostics = ((App)Application.Current).Services.GetRequiredService<IDiagnosticsService>();
        var result = diagnostics.ExportLogs();
        ExceptionTextBox.Text = result.IsSuccess ? "诊断包导出成功。" : result.Message;
    }

    private void OnContinue(object sender, RoutedEventArgs e) => Close();

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}
