namespace Pismolet.Web.Domain.Mailings;

public enum MessageType
{
    Transactional,
    Advertising
}

public static class MessageTypeLabels
{
    public static string ToRu(this MessageType type) => type == MessageType.Advertising ? "Рекламное" : "Информационное";
}

public sealed record MailingMessageDraft(
    string SenderName,
    string Subject,
    string Body,
    MessageType MessageType,
    DateTimeOffset UpdatedAt)
{
    public const int MaxSenderNameLength = 80;
    public const int MaxSubjectLength = 160;

    public static MailingMessageDraft Create(string senderName, string subject, string body, MessageType messageType, DateTimeOffset updatedAt)
    {
        senderName = senderName.Trim();
        subject = subject.Trim();
        body = body.Trim();

        if (string.IsNullOrWhiteSpace(senderName))
        {
            throw new ArgumentException("Укажите имя отправителя.", nameof(senderName));
        }

        if (senderName.Length > MaxSenderNameLength)
        {
            throw new ArgumentException($"Имя отправителя должно быть не длиннее {MaxSenderNameLength} символов.", nameof(senderName));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Укажите тему письма.", nameof(subject));
        }

        if (subject.Length > MaxSubjectLength)
        {
            throw new ArgumentException($"Тема письма должна быть не длиннее {MaxSubjectLength} символов.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Напишите текст письма.", nameof(body));
        }

        return new MailingMessageDraft(senderName, subject, body, messageType, updatedAt);
    }
}
