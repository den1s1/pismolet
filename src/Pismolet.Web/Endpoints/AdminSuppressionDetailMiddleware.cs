using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminSuppressionDetailMiddleware
{
    private const int DefaultDays = 365;
    private const int MaxDays = 365;
    private const int RowLimit = 50;

    public static IApplicationBuilder UseAdminSuppressionDetail(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var path = http.Request.Path.Value ?? string.Empty;
        if (!string.Equals(path.TrimEnd('/'), "/admin/suppressions/detail", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();
        var authorizationResult = await authorization.AuthorizeAsync(http.User, http, AdminEndpoints.AdminPolicyName);
        if (!authorizationResult.Succeeded)
        {
            http.Response.Redirect("/account/login");
            return;
        }

        var client = http.Request.Query["client"].ToString().Trim();
        var email = http.Request.Query["email"].ToString().Trim().ToLowerInvariant();
        var days = ReadDays(http);
        if (string.IsNullOrWhiteSpace(client) || string.IsNullOrWhiteSpace(email))
        {
            await RenderMissingInput(http, client, email);
            return;
        }

        var db = http.RequestServices.GetRequiredService<PismoletDbContext>();
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var suppressions = db.ClientSuppressions
            .AsNoTracking()
            .Where(x => x.ClientId == client && x.EmailNormalized == email)
            .OrderByDescending(x => x.CreatedAt)
            .Take(RowLimit)
            .Select(x => new SuppressionDetailRow(
                x.ClientId,
                x.EmailNormalized,
                x.Reason,
                x.SourceMailingId,
                x.SourceProviderMessageId,
                x.CreatedAt))
            .ToArray();
        var periodSuppressions = suppressions
            .Where(x => x.CreatedAt >= since)
            .ToArray();

        var mailings = http.RequestServices.GetRequiredService<IMailingRepository>();
        var ownerMailings = mailings.ListForOwner(client).ToDictionary(x => x.Id);
        var sendEvents = LoadRelatedEvents(http, suppressions, email);
        var eventRows = sendEvents
            .OrderByDescending(x => x.LastDeliveryEventAt ?? x.UpdatedAt)
            .Take(RowLimit)
            .ToArray();

        var rowsHtml = suppressions.Length == 0
            ? "<tr><td colspan='6'>Suppression для этого адреса не найден.</td></tr>"
            : string.Join(string.Empty, suppressions.Select(row => SuppressionRowHtml(row, ownerMailings, days)));
        var eventsHtml = eventRows.Length == 0
            ? "<tr><td colspan='6'>Связанные delivery-события не найдены.</td></tr>"
            : string.Join(string.Empty, eventRows.Select(EventRowHtml));
        var latestReason = suppressions.FirstOrDefault()?.Reason;

        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Suppression адреса</p>
                        <h1>{H(email)}</h1>
                        <p class='admin-muted'>Клиент: <a class='admin-link' href='/admin/delivery/client/{Uri.EscapeDataString(client)}?days={days}'>{H(client)}</a></p>
                    </div>
                    <a class='admin-export' href='/admin/suppressions?days={days}&client={Uri.EscapeDataString(client)}'>К suppression клиента</a>
                </div>
                <form class='admin-filters' method='get' action='/admin/suppressions/detail'>
                    <input type='hidden' name='client' value='{H(client)}'>
                    <input type='hidden' name='email' value='{H(email)}'>
                    <label>Период, дней<input type='number' min='1' max='{MaxDays}' name='days' value='{days}'></label>
                    <button class='admin-button' type='submit'>Обновить</button>
                </form>
                <div class='admin-stats'>
                    <div class='admin-stat'><b>{periodSuppressions.Length}</b><span>suppression за {days} дн.</span></div>
                    <div class='admin-stat'><b>{suppressions.Length}</b><span>всего записей</span></div>
                    <div class='admin-stat'><b>{eventRows.Length}</b><span>связанных delivery-событий</span></div>
                    <div class='admin-stat'><b>{H(latestReason ?? "-")}</b><span>последняя причина</span></div>
                </div>
                {RecommendationBlock(latestReason)}
                <div class='section-head'><div><p class='eyebrow'>История</p><h2>Записи suppression для адреса</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Причина</th><th>Рассылка</th><th>Provider ID</th><th>Дата</th><th>Что значит</th><th>Действие</th></tr></thead>
                        <tbody>{rowsHtml}</tbody>
                    </table>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Доставка</p><h2>Связанные delivery-события</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Рассылка</th><th>Доставка</th><th>Дата</th><th>Provider ID</th><th>Сводка</th><th>Действие</th></tr></thead>
                        <tbody>{eventsHtml}</tbody>
                    </table>
                </div>
                <p><a class='admin-link' href='/admin/suppressions?days={days}&client={Uri.EscapeDataString(client)}'>Вернуться к suppression клиента</a></p>
            </section>
            """;

        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(HtmlRenderer.Page("Админка - suppression адреса", body, authenticated: true));
    });

    private static SendEvent[] LoadRelatedEvents(HttpContext http, IReadOnlyCollection<SuppressionDetailRow> suppressions, string email)
    {
        var sendEvents = http.RequestServices.GetRequiredService<ISendEventRepository>();
        return suppressions
            .Where(x => x.SourceMailingId is not null)
            .Select(x => x.SourceMailingId!.Value)
            .Distinct()
            .SelectMany(mailingId => sendEvents.ListByMailingId(mailingId))
            .Where(x => string.Equals(x.RecipientEmail, email, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static int ReadDays(HttpContext http)
    {
        var raw = http.Request.Query["days"].ToString();
        return int.TryParse(raw, out var days) ? Math.Clamp(days, 1, MaxDays) : DefaultDays;
    }

    private static Task RenderMissingInput(HttpContext http, string client, string email)
    {
        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Suppression адреса</p>
                        <h1>Не хватает параметров</h1>
                        <p class='admin-muted'>Нужно передать client и email. Сейчас client='{H(client)}', email='{H(email)}'.</p>
                    </div>
                    <a class='admin-export' href='/admin/suppressions'>К suppression</a>
                </div>
            </section>
            """;
        http.Response.ContentType = "text/html; charset=utf-8";
        return http.Response.WriteAsync(HtmlRenderer.Page("Админка - suppression адреса", body, authenticated: true));
    }

    private static string SuppressionRowHtml(SuppressionDetailRow row, IReadOnlyDictionary<Guid, Mailing> mailings, int days)
    {
        var mailingTitle = row.SourceMailingId is null
            ? "-"
            : mailings.TryGetValue(row.SourceMailingId.Value, out var mailing)
                ? string.IsNullOrWhiteSpace(mailing.Subject) ? "Рассылка без темы" : mailing.Subject
                : $"Рассылка {row.SourceMailingId}";
        var mailingHtml = row.SourceMailingId is null
            ? "-"
            : $"<a class='admin-link' href='/admin/delivery/mailing/{row.SourceMailingId}?days={days}'>{H(mailingTitle)}</a><br><span class='admin-muted'>{row.SourceMailingId}</span>";
        var action = row.SourceMailingId is null
            ? "-"
            : $"<a class='admin-link' href='/admin/delivery/mailing/{row.SourceMailingId}?days={days}'>Открыть доставку</a>";
        return $"<tr><td><span class='admin-badge'>{H(row.Reason)}</span></td><td>{mailingHtml}</td><td>{H(row.ProviderMessageId)}</td><td>{FormatDate(row.CreatedAt)}</td><td>{H(ReasonHint(row.Reason))}</td><td>{action}</td></tr>";
    }

    private static string EventRowHtml(SendEvent row)
    {
        var action = $"<a class='admin-link' href='/admin/delivery/mailing/{row.MailingId}'>Открыть рассылку</a>";
        return $"<tr><td>{row.MailingId}</td><td><span class='admin-badge'>{H(row.DeliveryStatus.ToString())}</span></td><td>{FormatDate(row.LastDeliveryEventAt ?? row.UpdatedAt)}</td><td>{H(row.ProviderMessageId)}</td><td>{H(Shorten(row.LastDeliverySummary, 240))}</td><td>{action}</td></tr>";
    }

    private static string RecommendationBlock(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "<div class='section-head'><div><p class='eyebrow'>Что делать</p><h2>Адрес не найден в suppression</h2><p class='admin-muted'>Проверьте клиента, нормализацию email и выбранный период.</p></div></div>";
        }

        return $"<div class='section-head'><div><p class='eyebrow'>Что делать</p><h2>Рекомендация по адресу</h2><p class='admin-muted'>{H(ReasonHint(reason))}</p></div></div>";
    }

    private static string ReasonHint(string reason)
    {
        if (reason.Contains("HardBounce", StringComparison.OrdinalIgnoreCase))
        {
            return "Постоянная ошибка доставки. Адрес остаётся исключённым из будущих отправок клиента. Клиенту можно объяснить: принимающий сервер сообщил, что доставить письмо невозможно.";
        }

        if (reason.Contains("Complaint", StringComparison.OrdinalIgnoreCase))
        {
            return "Жалоба получателя. Адрес нельзя возвращать без ручной проверки: есть риск для репутации отправки и юридической модели согласий.";
        }

        if (reason.Contains("Unsubscribe", StringComparison.OrdinalIgnoreCase))
        {
            return "Получатель отказался от рассылок. Возвращать адрес в отправки нельзя без нового законного основания или нового согласия.";
        }

        if (reason.Contains("Manual", StringComparison.OrdinalIgnoreCase))
        {
            return "Ручное или внутреннее исключение. Перед снятием нужно понять, кто и зачем исключил адрес.";
        }

        return "Адрес исключён из будущих отправок клиента. Проверьте исходную рассылку и связанное delivery-событие.";
    }

    private static string Shorten(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm");

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record SuppressionDetailRow(string ClientId, string Email, string Reason, Guid? SourceMailingId, string? ProviderMessageId, DateTimeOffset CreatedAt);
}
