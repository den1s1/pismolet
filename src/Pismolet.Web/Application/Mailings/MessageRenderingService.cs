using System.Security.Cryptography;
using System.Text;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record RenderedMessagePreview(string PlainText, string UnsubscribeUrl, string ReasonBlock, string ServiceIdentifier);

public interface IUnsubscribeTokenService
{
    string Generate(Guid mailingId, string recipientEmail);
}

public interface IMessageRenderingService
{
    RenderedMessagePreview RenderPreview(Mailing mailing);
}

public sealed class DevUnsubscribeTokenService : IUnsubscribeTokenService
{
    public string Generate(Guid mailingId, string recipientEmail)
    {
        var raw = $"{mailingId:N}:{recipientEmail.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}

public sealed class MessageRenderingService(IUnsubscribeTokenService tokens) : IMessageRenderingService
{
    public RenderedMessagePreview RenderPreview(Mailing mailing)
    {
        if (mailing.MessageDraft is null)
        {
            return new RenderedMessagePreview(string.Empty, string.Empty, string.Empty, mailing.PublicId);
        }

        var firstRecipient = mailing.Recipients.FirstOrDefault(x => x.Status == RecipientStatus.Accepted)?.Email ?? "recipient@example.test";
        var token = tokens.Generate(mailing.Id, firstRecipient);
        var unsubscribeUrl = $"/unsubscribe/{token}";
        var source = mailing.Declaration?.BaseSource.ToRu() ?? "загруженной базы адресов";
        var reason = $"Почему вы получили это письмо: ваш адрес находится в базе «{source}», которую отправитель подтвердил перед рассылкой.";
        var serviceId = $"Служебный идентификатор рассылки: {mailing.PublicId}";
        var plain = string.Join("\n\n", mailing.MessageDraft.Body, reason, $"Отписаться от всех рассылок через сервис: {unsubscribeUrl}", serviceId);

        return new RenderedMessagePreview(plain, unsubscribeUrl, reason, serviceId);
    }
}
