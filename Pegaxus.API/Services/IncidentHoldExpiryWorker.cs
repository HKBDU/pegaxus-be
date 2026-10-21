using Pegaxus.Service.IncidentService;

namespace Pegaxus.API.Services;

public sealed class IncidentHoldExpiryWorker(IServiceScopeFactory scopes, ILogger<IncidentHoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IIncidentService>().QueueExpiredHoldsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Incident hold expiry cycle failed; next cycle will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
