using Pismolet.Web.Domain.Mailings;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class HtmlMessageSanitizerTests
{
    [Fact]
    public void Sanitize_keeps_simple_email_formatting()
    {
        var html = "<p>Привет, <b>друг</b> и <i>читатель</i>. <span style=\"color:#FF0000;font-size:18px\">Важно</span> <a href=\"https://example.ru/page\">ссылка</a></p>";

        var result = HtmlMessageSanitizer.Sanitize(html);

        Assert.Equal("<p>Привет, <strong>друг</strong> и <em>читатель</em>. <span style=\"color:#ff0000;font-size:18px\">Важно</span> <a href=\"https://example.ru/page\">ссылка</a></p>", result);
    }

    [Fact]
    public void Sanitize_removes_scripts_events_dangerous_links_and_unsafe_styles()
    {
        var html = """
<p onclick="alert(1)">Обычный текст<script>alert('x')</script><style>body{display:none}</style></p>
<a href="javascript:alert(1)">опасная ссылка</a>
<a href="mailto:hello@example.ru">почта</a>
<span style="color:#00ff00;font-size:18px;background-image:url(javascript:evil)">стиль</span>
<iframe src="https://evil.test"></iframe>
""";

        var result = HtmlMessageSanitizer.Sanitize(html);

        Assert.Contains("<p>Обычный текст</p>", result);
        Assert.Contains("<a>опасная ссылка</a>", result);
        Assert.Contains("<a href=\"mailto:hello@example.ru\">почта</a>", result);
        Assert.Contains("<span>стиль</span>", result);
        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style>", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", result, StringComparison.OrdinalIgnoreCase);
    }
}
