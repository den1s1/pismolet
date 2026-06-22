using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Database;

[Table("postfix_delivery_events")]
[Index(nameof(QueueId), nameof(RecipientEmail), nameof(Status), nameof(OccurredAt), IsUnique = true)]
[Index(nameof(OccurredAt))]
[Index(nameof(RecipientEmail), nameof(OccurredAt))]
[Index(nameof(DeliveryStatus), nameof(OccurredAt))]
public sealed class PostfixDeliveryEventEntity
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string QueueId { get; set; } = string.Empty;

    [MaxLength(254)]
    public string RecipientEmail { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(40)]
    public string DeliveryStatus { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? Dsn { get; set; }

    [MaxLength(512)]
    public string? Relay { get; set; }

    [MaxLength(2000)]
    public string? Diagnostic { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
