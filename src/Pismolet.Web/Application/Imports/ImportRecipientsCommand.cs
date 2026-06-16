using Pismolet.Web.Application.Common;

namespace Pismolet.Web.Application.Imports;

public sealed record ImportRecipientsCommand(
    string UserEmail,
    Guid MailingId,
    string FileName,
    Stream Content,
    RequestMetadata Request);
