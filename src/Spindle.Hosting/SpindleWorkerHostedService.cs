using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Spindle.Hosting;

public sealed class SpindleWorkerHostedService(
    ISpindleRuntimePump pump,
    IOptions<SpindleHostOptions> options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await pump.RunOnceAsync(stoppingToken)
                .ConfigureAwait(false);

            if (!result.HasProgress)
            {
                if (result is { ScheduledFlows: > 0 } or { InFlightFlows: > 0 })
                {
                    await pump
                        .WaitForWakeupAsync(options.Value.PollInterval, stoppingToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(options.Value.PollInterval, stoppingToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
