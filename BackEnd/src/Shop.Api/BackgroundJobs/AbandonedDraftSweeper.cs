using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Shop.Application.Orders;

namespace Shop.Api.BackgroundJobs;

public sealed class DraftSweeperOptions
{
    public const string SectionName = "DraftSweeper";

    public bool Enabled { get; init; } = true;

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:01", "30.00:00:00")]
    public TimeSpan DraftTimeout { get; init; } = TimeSpan.FromMinutes(30);

    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 100;
}

public sealed class AbandonedDraftSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<DraftSweeperOptions> options,
    TimeProvider timeProvider,
    ILogger<AbandonedDraftSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DraftSweeperOptions settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Abandoned-draft sweeper is disabled");
            return;
        }

        logger.LogInformation(
            "Abandoned-draft sweeper started: interval {Interval}, draft timeout {Timeout}, batch size {BatchSize}",
            settings.Interval, settings.DraftTimeout, settings.BatchSize);

        using PeriodicTimer timer = new PeriodicTimer(settings.Interval, timeProvider);
        do
        {
            await RunOnceAsync(settings, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(DraftSweeperOptions settings, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IExpireAbandonedDraftsUseCase expireDrafts = scope.ServiceProvider.GetRequiredService<IExpireAbandonedDraftsUseCase>();

            long started = timeProvider.GetTimestamp();
            int expired = await expireDrafts.ExecuteAsync(settings.DraftTimeout, settings.BatchSize, ct);
            logger.LogInformation("Abandoned-draft sweep finished: {Expired} draft(s) expired in {Elapsed:0} ms",
                expired, timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Abandoned-draft sweep failed; will retry on the next tick");
        }
    }
}
