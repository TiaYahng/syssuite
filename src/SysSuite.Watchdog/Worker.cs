using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SysSuite.Watchdog;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> logger;

    public Worker(ILogger<Worker> logger) => this.logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.Log(LogLevel.Information, new EventId(1001, nameof(ExecuteAsync)), "Started", null, static (_, _) => "Watchdog started");
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
