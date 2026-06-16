namespace Pismolet.Web.Application.Mailings;

public sealed record CreateMailingCommand(string OwnerEmail, string Subject);
