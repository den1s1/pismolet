namespace Pismolet.Web.Application.Mailings;

public sealed record InboundReplyRawMessage(byte[] RawMime, string? EnvelopeRecipient, string SourceId);

public interface IInboundReplyMimeParser
{
    Task<EmailProviderInboundParseResult> ParseAsync(InboundReplyRawMessage message, CancellationToken cancellationToken);
}
