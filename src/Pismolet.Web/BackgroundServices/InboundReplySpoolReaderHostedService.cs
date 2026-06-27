using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.BackgroundServices;

public sealed class InboundReplySpoolReaderHostedService(
    InboundReplySpoolOptions options,
    ILogger<InboundReplySpoolReaderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Inbound reply spool reader is disabled.");
            return;
        }

        logger.LogInformation(
            "Inbound reply spool reader started. spoolPath={SpoolPath} pollIntervalSeconds={PollIntervalSeconds} maxMessageBytes={MaxMessageBytes}",
            options.SpoolPath,
            options.PollIntervalSeconds,
            options.MaxMessageBytes);

        EnsureSpoolDirectories();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                LogSpoolSnapshot();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbound reply spool reader skeleton iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(InboundReplySpoolOptions.MinPollIntervalSeconds, options.PollIntervalSeconds)), stoppingToken);
        }
    }

    private void EnsureSpoolDirectories()
    {
        Directory.CreateDirectory(options.IncomingPath);
        Directory.CreateDirectory(options.ProcessingPath);
        Directory.CreateDirectory(options.ProcessedPath);
        Directory.CreateDirectory(options.FailedPath);
    }

    private void LogSpoolSnapshot()
    {
        if (!Directory.Exists(options.IncomingPath))
        {
            logger.LogWarning("Inbound reply incoming spool directory is missing. incomingPath={IncomingPath}", options.IncomingPath);
            return;
        }

        var incomingCount = Directory.EnumerateFiles(options.IncomingPath, "*.eml", SearchOption.TopDirectoryOnly).Take(options.MaxFilesPerPoll + 1).Count();
        logger.LogDebug(
            "Inbound reply spool skeleton checked incoming directory. incomingPath={IncomingPath} visibleEmlFiles={VisibleEmlFiles}",
            options.IncomingPath,
            incomingCount);
    }
}
