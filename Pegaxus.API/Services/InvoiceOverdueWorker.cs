using Pegaxus.Service.InvoiceService;

namespace Pegaxus.API.Services;

public sealed class InvoiceOverdueWorker(IServiceScopeFactory scopes, ILogger<InvoiceOverdueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var marked = await scope.ServiceProvider.GetRequiredService<IInvoiceService>().MarkOverdueAsync(stoppingToken);
                if (marked > 0) logger.LogInformation("Marked {Count} invoice(s) overdue.", marked);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Invoice overdue cycle failed; next cycle will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
