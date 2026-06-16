namespace Pismolet.Web.Domain.Mail;

public sealed record FakeMail(string To, string Subject, string Link, DateTimeOffset CreatedAt);
