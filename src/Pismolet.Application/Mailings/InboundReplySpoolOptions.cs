namespace Pismolet.Web.Application.Mailings;

public sealed record InboundReplySpoolOptions(
    bool Enabled,
    string SpoolPath,
    int PollIntervalSeconds,
    long MaxMessageBytes,
    int ProcessedRetentionDays,
    int FailedRetentionDays,
    int MaxFilesPerPoll)
{
    public const int MinPollIntervalSeconds = 5;
    public const int MaxPollIntervalSeconds = 3600;
    public const long MinMessageBytes = 1024;
    public const long MaxAllowedMessageBytes = 50L * 1024L * 1024L;
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 365;
    public const int MinFilesPerPoll = 1;
    public const int MaxFilesPerPollLimit = 500;

    public static InboundReplySpoolOptions DevelopmentDefault { get; } = new(
        Enabled: false,
        SpoolPath: Path.Combine(Path.GetTempPath(), "pismolet-inbound-replies"),
        PollIntervalSeconds: 10,
        MaxMessageBytes: 10L * 1024L * 1024L,
        ProcessedRetentionDays: 7,
        FailedRetentionDays: 30,
        MaxFilesPerPoll: 50);

    public string IncomingPath => Path.Combine(SpoolPath, "incoming");

    public string ProcessingPath => Path.Combine(SpoolPath, "processing");

    public string ProcessedPath => Path.Combine(SpoolPath, "processed");

    public string FailedPath => Path.Combine(SpoolPath, "failed");
}
