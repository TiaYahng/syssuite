using System.IO;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using SysSuite.Watchdog;
using SysSuite.Watchdog.Ipc;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<PipeServer>();
builder.Services.AddSingleton<LeaseRegistry>();

if (args.Contains("--autostart"))
{
    using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
    runKey?.SetValue("SysSuite.Watchdog", Environment.ProcessPath ?? string.Empty);
}

using var mutex = new Mutex(true, @"Local\SysSuite.Watchdog", out var isFirstInstance);
if (!isFirstInstance) return;

var host = builder.Build();
var pipeServer = host.Services.GetRequiredService<PipeServer>();
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new WatchdogApplicationContext(host, pipeServer));
