using Pismolet.Web.Endpoints;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class AdminModerationMessagePreviewMiddlewareTests
{
    [Fact]
    public void TransformHtml_hides_message_until_preview_is_opened()
    {
        const string html = "<section><h2>Письмо</h2><p><strong>Отправитель:</strong> Тест</p><p><strong>Тема:</strong> Тема</p><pre>&lt;h1&gt;Письмо&lt;/h1&gt;</pre><h2>Служебные блоки</h2><p>Причина</p></section>";

        var transformed = AdminModerationMessagePreviewMiddlewareExtensions.TransformHtml(html);

        Assert.Contains("<details class='admin-message-preview'>", transformed, StringComparison.Ordinal);
        Assert.Contains("<summary class='admin-button'>Просмотр письма</summary>", transformed, StringComparison.Ordinal);
        Assert.Contains("<div class='admin-message-preview-content'><p><strong>Отправитель:</strong> Тест</p>", transformed, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>Письмо</h2>", transformed, StringComparison.Ordinal);
        Assert.Contains("<h2>Служебные блоки</h2>", transformed, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformHtml_leaves_other_pages_unchanged()
    {
        const string html = "<section><h1>Очередь модерации</h1></section>";

        var transformed = AdminModerationMessagePreviewMiddlewareExtensions.TransformHtml(html);

        Assert.Equal(html, transformed);
    }
}
