using Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Services;

public sealed class MissingDocumentReminderWorker(IServiceScopeFactory scopes, ILogger<MissingDocumentReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IService>();
                await service.DeliverPendingMissingDocumentRemindersAsync(stoppingToken);
                await service.QueueAutomaticMissingDocumentRemindersAsync(stoppingToken);
                await service.DeliverPendingMissingDocumentRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Missing-document reminder cycle failed; next cycle will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
