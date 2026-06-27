using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class PostfixRawMimeInboundReplyParser : IInboundReplyMimeParser
{
    public Task<EmailProviderInboundParseResult> ParseAsync(InboundReplyRawMessage message, CancellationToken cancellationToken)
    {
        if (message.RawMime.Length == 0)
        {
            return Task.FromResult(EmailProviderInboundParseResult.Failure("Пустое входящее письмо."));
        }

        return Task.FromResult(EmailProviderInboundParseResult.Failure("MIME parser ещё не подключён."));
    }
}
