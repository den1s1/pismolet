namespace Pismolet.Web.Application.Mailings;

public static class InboundReplyAutoReplyDetector
{
    public static bool ShouldIgnore(EmailProviderInboundEvent inbound)
    {
        if (string.IsNullOrWhiteSpace(inbound.FromEmail))
        {
            return true;
        }

        if (LooksLikeSystemSender(inbound.FromEmail))
        {
            return true;
        }

        if (LooksLikeSystemSubject(inbound.Subject))
        {
            return true;
        }

        return inbound.Headers.Any(header => LooksLikeAutoReplyHeader(header.Key, header.Value));
    }

    private static bool LooksLikeSystemSender(string fromEmail)
    {
        var value = fromEmail.Trim().ToLowerInvariant();
        var localPart = value.Split(Convert.ToChar(64), 2)[0];
        return localPart.Contains("mailer-daemon", StringComparison.OrdinalIgnoreCase) ||
            localPart.Contains("postmaster", StringComparison.OrdinalIgnoreCase) ||
            localPart.Contains("no-reply", StringComparison.OrdinalIgnoreCase) ||
            localPart.Contains("noreply", StringComparison.OrdinalIgnoreCase) ||
            localPart.Contains("donotreply", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("reply.localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSystemSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        var value = subject.Trim();
        return value.Contains("undelivered", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("delivery status notification", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("delivery failure", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("out of office", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("automatic reply", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("auto reply", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("автоответ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAutoReplyHeader(string key, string value)
    {
        if (key.Equals("Auto-Submitted", StringComparison.OrdinalIgnoreCase))
        {
            return !value.Equals("no", StringComparison.OrdinalIgnoreCase);
        }

        if (key.Equals("Precedence", StringComparison.OrdinalIgnoreCase))
        {
            return value.Equals("bulk", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("list", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("junk", StringComparison.OrdinalIgnoreCase);
        }

        return key.Equals("X-Auto-Response-Suppress", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("X-Autoreply", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("X-Autorespond", StringComparison.OrdinalIgnoreCase) ||
            (key.Equals("Return-Path", StringComparison.OrdinalIgnoreCase) && value.Trim().Equals("<>", StringComparison.Ordinal));
    }
}
