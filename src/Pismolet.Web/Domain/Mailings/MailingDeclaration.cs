namespace Pismolet.Web.Domain.Mailings;

public enum BaseSource
{
    Customers,
    EventParticipants,
    FormSubscribers,
    OrganizationMembers,
    Other
}

public static class BaseSourceLabels
{
    public static IReadOnlyDictionary<BaseSource, string> All { get; } = new Dictionary<BaseSource, string>
    {
        [BaseSource.Customers] = "Клиенты или покупатели",
        [BaseSource.EventParticipants] = "Участники мероприятия",
        [BaseSource.FormSubscribers] = "Подписчики формы",
        [BaseSource.OrganizationMembers] = "Члены организации или сообщества",
        [BaseSource.Other] = "Другое"
    };

    public static string ToRu(this BaseSource source) => All.GetValueOrDefault(source, "Другое");
}

public sealed record MailingDeclaration(
    Guid MailingId,
    string UserEmail,
    BaseSource BaseSource,
    bool IsBaseLegalityConfirmed,
    bool IsAdvertisingConsentConfirmed,
    string DeclarationVersion,
    DateTimeOffset CreatedAt,
    string Ip,
    string UserAgent)
{
    public bool IsValidFor(MessageType messageType) => IsBaseLegalityConfirmed &&
        (messageType != MessageType.Advertising || IsAdvertisingConsentConfirmed);
}
