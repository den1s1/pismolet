using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class MailingRecipientManagementEndpoints
{
    public static IEndpointRouteBuilder MapMailingRecipientManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mailings/{id:guid}/recipients/add", AddRecipient).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/recipients/remove", RemoveRecipient).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> AddRecipient(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations)
    {
        var form = await http.Request.ReadFormAsync();
        var newEmail = form["email"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(newEmail))
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return await Reimport(id, http, mailings, imports, declarations, rows => rows.Add(new RecipientSourceRow(NextRowNumber(rows), newEmail)));
    }

    private static async Task<IResult> RemoveRecipient(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations)
    {
        var form = await http.Request.ReadFormAsync();
        var removedEmail = form["email"].ToString().Trim();
        var rowNumber = int.TryParse(form["rowNumber"].ToString(), out var parsedRow) ? parsedRow : 0;
        return await Reimport(id, http, mailings, imports, declarations, rows =>
        {
            var index = rowNumber > 0
                ? rows.FindIndex(row => row.RowNumber == rowNumber)
                : rows.FindIndex(row => string.Equals(row.Email, removedEmail, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                rows.RemoveAt(index);
            }
        });
    }

    private static async Task<IResult> Reimport(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations, Action<List<RecipientSourceRow>> change)
    {
        var ownerEmail = http.User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            return Results.Redirect("/dashboard");
        }

        var mailing = mailings.GetForOwner(id, ownerEmail);
        if (mailing is null)
        {
            return Results.Redirect("/dashboard");
        }

        var rows = CurrentSourceRows(mailing).ToList();
        change(rows);
        var csv = "email\n" + string.Join('\n', rows.Select(row => row.Email).Where(email => !string.IsNullOrWhiteSpace(email)));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await imports.ImportAsync(new ImportRecipientsCommand(ownerEmail, id, "manual-addresses.csv", stream, Request(http)));
        var refreshed = mailings.GetForOwner(id, ownerEmail) ?? mailing;
        PreserveDeclaration(ownerEmail, id, refreshed, declarations, http);
        return Results.Redirect($"/mailings/{id}/recipients");
    }

    private static IEnumerable<RecipientSourceRow> CurrentSourceRows(Mailing mailing)
    {
        var fallbackOrder = 0;
        foreach (var recipient in mailing.Recipients.OrderBy(x => x.RowNumber > 0 ? x.RowNumber : int.MaxValue))
        {
            fallbackOrder++;
            var email = string.IsNullOrWhiteSpace(recipient.SourceEmail) ? recipient.Email : recipient.SourceEmail;
            if (!string.IsNullOrWhiteSpace(email))
            {
                yield return new RecipientSourceRow(recipient.RowNumber > 0 ? recipient.RowNumber : fallbackOrder + 1, email);
            }
        }
    }

    private static void PreserveDeclaration(string ownerEmail, Guid mailingId, Mailing mailing, IMailingDeclarationService declarations, HttpContext http)
    {
        if (mailing.Declaration is null)
        {
            return;
        }

        var messageType = mailing.MessageDraft?.MessageType ?? MessageType.Transactional;
        declarations.Confirm(new ConfirmMailingDeclarationCommand(
            ownerEmail,
            mailingId,
            mailing.Declaration.BaseSource,
            mailing.Declaration.IsBaseLegalityConfirmed,
            mailing.Declaration.IsAdvertisingConsentConfirmed,
            messageType,
            Request(http)));
    }

    private static int NextRowNumber(IReadOnlyCollection<RecipientSourceRow> rows) => rows.Count == 0 ? 2 : rows.Max(row => row.RowNumber) + 1;

    private static RequestMetadata Request(HttpContext http) => new(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());

    private sealed record RecipientSourceRow(int RowNumber, string Email);
}
