namespace Pismolet.Web.Infrastructure.Mail;

public sealed record PostfixDeliveryLogReaderOptions(
    string LogPath,
    string CursorPath,
    int Year,
    TimeSpan UtcOffset,
    bool StartAtEndWhenCursorMissing = true)
{
    public static PostfixDeliveryLogReaderOptions ProductionDefault => new(
        "/var/log/mail.log",
        "/var/lib/pismolet/postfix-delivery.cursor",
        DateTimeOffset.UtcNow.Year,
        TimeSpan.Zero,
        StartAtEndWhenCursorMissing: true);
}

public sealed record PostfixDeliveryLogReaderResult(
    bool LogExists,
    long PreviousPosition,
    long NewPosition,
    int LinesRead,
    bool CursorInitialized,
    bool CursorReset,
    PostfixDeliveryLogIngestionResult Ingestion)
{
    public static PostfixDeliveryLogReaderResult MissingLog() => new(
        LogExists: false,
        PreviousPosition: 0,
        NewPosition: 0,
        LinesRead: 0,
        CursorInitialized: false,
        CursorReset: false,
        Ingestion: new PostfixDeliveryLogIngestionResult(0, 0, 0, 0, 0, 0));
}

public sealed class PostfixDeliveryLogReaderService(
    PostfixDeliveryLogReaderOptions options,
    PostfixDeliveryLogIngestionService ingestionService)
{
    public PostfixDeliveryLogReaderResult ReadNewLines()
    {
        if (!File.Exists(options.LogPath))
        {
            return PostfixDeliveryLogReaderResult.MissingLog();
        }

        var logLength = new FileInfo(options.LogPath).Length;
        var cursorExists = File.Exists(options.CursorPath);

        if (!cursorExists && options.StartAtEndWhenCursorMissing)
        {
            WriteCursor(logLength);
            return new PostfixDeliveryLogReaderResult(
                LogExists: true,
                PreviousPosition: logLength,
                NewPosition: logLength,
                LinesRead: 0,
                CursorInitialized: true,
                CursorReset: false,
                Ingestion: new PostfixDeliveryLogIngestionResult(0, 0, 0, 0, 0, 0));
        }

        var previousPosition = cursorExists ? ReadCursor() : 0;
        var cursorReset = false;
        if (previousPosition < 0 || previousPosition > logLength)
        {
            previousPosition = 0;
            cursorReset = true;
        }

        if (previousPosition == logLength)
        {
            return new PostfixDeliveryLogReaderResult(
                LogExists: true,
                PreviousPosition: previousPosition,
                NewPosition: logLength,
                LinesRead: 0,
                CursorInitialized: false,
                CursorReset: cursorReset,
                Ingestion: new PostfixDeliveryLogIngestionResult(0, 0, 0, 0, 0, 0));
        }

        using var stream = new FileStream(options.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(previousPosition, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var newPosition = stream.Position;
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ingestion = ingestionService.IngestLines(lines, options.Year, options.UtcOffset);

        WriteCursor(newPosition);
        return new PostfixDeliveryLogReaderResult(
            LogExists: true,
            PreviousPosition: previousPosition,
            NewPosition: newPosition,
            LinesRead: lines.Length,
            CursorInitialized: false,
            CursorReset: cursorReset,
            Ingestion: ingestion);
    }

    private long ReadCursor()
    {
        var raw = File.ReadAllText(options.CursorPath).Trim();
        return long.TryParse(raw, out var position) ? position : 0;
    }

    private void WriteCursor(long position)
    {
        var directory = Path.GetDirectoryName(options.CursorPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(options.CursorPath, position.ToString());
    }
}
