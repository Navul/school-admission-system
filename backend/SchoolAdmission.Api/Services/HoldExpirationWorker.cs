using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Services;

public sealed class HoldExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<HoldExpirationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using IServiceScope scope = scopeFactory.CreateAsyncScope();
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                int count = await db.Holds
                    .Where(x => x.Status == HoldStatus.Active && x.ExpiresAt <= DateTimeOffset.UtcNow)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, HoldStatus.Expired),
                        stoppingToken);
                if (count > 0)
                    logger.LogInformation("Expired {Count} seat holds", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to expire seat holds");
            }
        }
    }
}

