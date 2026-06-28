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
    public void Validate_allows_common_email_html()
    {
        var html = """
<!doctype html>
<html>
<head><meta charset="utf-8"><title>Письмо</title></head>
<body>
  <table width="600" cellpadding="0" cellspacing="0">
    <tr><td style="color:#333333;font-size:18px"><h1>Здравствуйте</h1><p>Текст письма</p><img src="https://example.ru/pic.png" width="120" alt="Картинка"></td></tr>
  </table>
  <p><a href="tel:89163112380">8 (916) 311-23-80</a></p>
</body>
</html>
""";

        var result = HtmlMessageSanitizer.Validate(html);

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void Validate_rejects_scripts_events_dangerous_links_and_unsafe_styles()
    {
        AssertValidationError("<p onclick=\"alert(1)\">Обычный текст</p>", "onclick");
        AssertValidationError("<script>alert('x')</script>", "script");
        AssertValidationError("<style>body{display:none}</style>", "style");
        AssertValidationError("<a href=\"javascript:alert(1)\">опасная ссылка</a>", "ссыл");
        AssertValidationError("<span style=\"background-image:url(javascript:evil)\">стиль</span>", "CSS");
        AssertValidationError("<iframe src=\"https://evil.test\"></iframe>", "iframe");
    }

    private static void AssertValidationError(string html, string expected)
    {
        var result = HtmlMessageSanitizer.Validate(html);

        Assert.False(result.Ok);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
