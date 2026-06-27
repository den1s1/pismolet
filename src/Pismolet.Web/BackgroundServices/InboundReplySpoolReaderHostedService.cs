using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.BackgroundServices;

public sealed class InboundReplySpoolReaderHostedService(
    InboundReplySpoolOptions options,
    IServiceScopeFactory scopeFactory,
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
                await ProcessIncomingFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbound reply spool reader iteration failed.");
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

    private async Task ProcessIncomingFilesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(options.IncomingPath))
        {
            logger.LogWarning("Inbound reply incoming spool directory is missing. incomingPath={IncomingPath}", options.IncomingPath);
            return;
        }

        var files = Directory.EnumerateFiles(options.IncomingPath, "*.eml", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(InboundReplySpoolOptions.MinFilesPerPoll, options.MaxFilesPerPoll))
            .ToArray();

        foreach (var incomingPath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOneFileAsync(incomingPath, cancellationToken);
        }
    }

    private async Task ProcessOneFileAsync(string incomingPath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(incomingPath);
        var processingPath = Path.Combine(options.ProcessingPath, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{fileName}");

        try
        {
            var fileInfo = new FileInfo(incomingPath);
            if (!fileInfo.Exists)
            {
                return;
            }

            if (fileInfo.Length > options.MaxMessageBytes)
            {
                logger.LogWarning("Inbound reply file is too large. fileName={FileName} bytes={Bytes} maxBytes={MaxBytes}", fileName, fileInfo.Length, options.MaxMessageBytes);
                MoveToFailed(incomingPath, "file_too_large");
                return;
            }

            File.Move(incomingPath, processingPath, overwrite: false);
            logger.LogInformation("Inbound reply file moved to processing. fileName={FileName}", fileName);

            var raw = await File.ReadAllBytesAsync(processingPath, cancellationToken);
            using var scope = scopeFactory.CreateScope();
            var parser = scope.ServiceProvider.GetRequiredService<IInboundReplyMimeParser>();
            var processor = scope.ServiceProvider.GetRequiredService<IInboundReplyProcessingService>();
            var parsed = await parser.ParseAsync(new InboundReplyRawMessage(raw, EnvelopeRecipient: null, SourceId: fileName), cancellationToken);
            if (!parsed.Ok || parsed.Event is null)
            {
                logger.LogWarning("Inbound reply parse failed. fileName={FileName} error={Error}", fileName, parsed.Error);
                MoveToFailed(processingPath, "parse_failed");
                return;
            }

            var result = await processor.ProcessAsync(parsed.Event, new RequestMetadata("spool", "inbound-reply-spool-reader"), cancellationToken);
            logger.LogInformation("Inbound reply file processed. fileName={FileName} status={Status} correlationId={CorrelationId}", fileName, result.Status, result.CorrelationId);
            MoveToProcessed(processingPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Inbound reply file IO processing failed. fileName={FileName}", fileName);
            if (File.Exists(processingPath))
            {
                MoveToFailed(processingPath, "io_error");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Inbound reply file processing failed. fileName={FileName}", fileName);
            if (File.Exists(processingPath))
            {
                MoveToFailed(processingPath, "processing_error");
            }
        }
    }

    private void MoveToProcessed(string path)
    {
        Directory.CreateDirectory(options.ProcessedPath);
        var target = Path.Combine(options.ProcessedPath, Path.GetFileName(path));
        File.Move(path, target, overwrite: true);
    }

    private void MoveToFailed(string path, string reason)
    {
        Directory.CreateDirectory(options.FailedPath);
        var target = Path.Combine(options.FailedPath, Path.GetFileName(path));
        File.Move(path, target, overwrite: true);
        File.WriteAllText(target + ".error", reason);
    }
}
