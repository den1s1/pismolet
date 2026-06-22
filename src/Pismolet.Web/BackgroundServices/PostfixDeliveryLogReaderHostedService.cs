using Pismolet.Web.Infrastructure.Mail;

namespace Pismolet.Web.BackgroundServices;

public sealed class PostfixDeliveryLogReaderHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PostfixDeliveryLogReaderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(PostfixDeliveryAutomationSettings.DefaultIntervalSeconds);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var settingsRepository = scope.ServiceProvider.GetRequiredService<IPostfixDeliveryAutomationSettingsRepository>();
                var settings = settingsRepository.Get().Normalize();
                delay = TimeSpan.FromSeconds(settings.IntervalSeconds);

                if (settings.Enabled)
                {
                    var reader = scope.ServiceProvider.GetRequiredService<PostfixDeliveryLogReaderService>();
                    var result = reader.ReadNewLines();

                    if (result.LinesRead > 0 || result.Ingestion.Stored > 0 || result.Ingestion.UpdatedSendEvents > 0)
                    {
                        logger.LogInformation(
                            "Postfix delivery log reader run: lines={LinesRead} parsed={Parsed} stored={Stored} matched={Matched} updated={Updated} ignored={Ignored}",
                            result.LinesRead,
                            result.Ingestion.Parsed,
                            result.Ingestion.Stored,
                            result.Ingestion.MatchedSendEvents,
                            result.Ingestion.UpdatedSendEvents,
                            result.Ingestion.Ignored);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Postfix delivery log reader run failed.");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
