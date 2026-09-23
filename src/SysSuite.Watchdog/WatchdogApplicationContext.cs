using Microsoft.Extensions.Hosting;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using SysSuite.Watchdog.Ipc;

namespace SysSuite.Watchdog;

internal sealed class WatchdogApplicationContext : ApplicationContext
{
    private readonly IHost host;
    private readonly PipeServer pipeServer;
    private readonly Form hiddenForm;
    private readonly NotifyIcon icon;
    private bool paused;
    private bool exiting;

    public WatchdogApplicationContext(IHost host, PipeServer pipeServer)
    {
        this.host = host;
        this.pipeServer = pipeServer;
        hiddenForm = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.None,
            Opacity = 0
        };
        MainForm = hiddenForm;

        icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "SysSuite Watchdog",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主程序", null, (_, _) => OpenMainApp());
        menu.Items.Add("暂停监控", null, (_, _) =>
        {
            paused = !paused;
            icon.Text = paused ? "SysSuite Watchdog 已暂停" : "SysSuite Watchdog";
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _ = ExitAsync());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => OpenMainApp();

        _ = host.StartAsync();
        _ = pipeServer.StartAsync(frame =>
        {
            if (!paused && frame.Type == "COMMAND")
            {
                OpenMainApp();
            }
        });
    }

    /// <summary>从嵌入资源加载品牌图标；缺失时退回系统盾牌图标，保证托盘不因资源问题消失。</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(WatchdogApplicationContext).Assembly
                .GetManifestResourceStream("SysSuite.Watchdog.App.ico");
            if (stream is null)
            {
                return SystemIcons.Shield;
            }

            // 多尺寸 ICO 中按托盘实际尺寸挑选，避免系统二次缩放导致模糊
            return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (ArgumentException)
        {
            return SystemIcons.Shield;
        }
    }

    private static void OpenMainApp()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SysSuite.UI.exe"),
            Path.Combine(AppContext.BaseDirectory, "SysSuite.UI", "bin", "Release", "net8.0-windows", "SysSuite.UI.exe")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        _ = ExitAsync();
    }

    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        icon.Visible = false;
        await pipeServer.DisposeAsync();
        await host.StopAsync(TimeSpan.FromSeconds(5));
        hiddenForm.Close();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            icon.Dispose();
            hiddenForm.Dispose();
        }
        base.Dispose(disposing);
    }
}
