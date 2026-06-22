using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Persistence;

public interface IPostfixDeliveryEventRepository
{
    PostfixDeliveryEvent AddIfNotExists(PostfixDeliveryEvent deliveryEvent);

    PostfixDeliveryEvent? GetByQueueIdAndRecipient(string queueId, string recipientEmail);

    IReadOnlyCollection<PostfixDeliveryEvent> ListRecent(int limit);

    IReadOnlyCollection<PostfixDeliveryEvent> ListByRecipient(string recipientEmail, int limit);
}
