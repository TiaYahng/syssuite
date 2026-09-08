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
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "SysSuite Watchdog",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("?????", null, (_, _) => OpenMainApp());
        menu.Items.Add("????", null, (_, _) =>
        {
            paused = !paused;
            icon.Text = paused ? "SysSuite Watchdog?????" : "SysSuite Watchdog";
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("??", null, (_, _) => _ = ExitAsync());
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
