using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfUserRepository(PismoletDbContext db) : IUserRepository
{
    public bool Exists(string email) => db.Users.Any(x => x.NormalizedEmail == N(email));
    public bool PhoneExists(string phone) { var normalized = NormalizePhone(phone); return !string.IsNullOrWhiteSpace(normalized) && ListAll().Any(user => NormalizePhone(user.Phone) == normalized); }
    public bool TryAdd(UserAccount user) { if (Exists(user.Email) || PhoneExists(user.Phone)) return false; db.Users.Add(ToEntity(user)); db.SaveChanges(); SavePhone(user.Email, user.Phone); return true; }
    public UserAccount? GetByEmail(string email) => db.Users.AsNoTracking().FirstOrDefault(x => x.NormalizedEmail == N(email)) is { } x ? WithPhone(ToDomain(x)) : null;
    public UserAccount? FindByConfirmationToken(string token) => db.Users.AsNoTracking().FirstOrDefault(x => x.ConfirmationToken == token) is { } x ? WithPhone(ToDomain(x)) : null;
    public IReadOnlyCollection<UserAccount> ListAll() => db.Users.AsNoTracking().OrderBy(x => x.NormalizedEmail).ToArray().Select(ToDomain).Select(WithPhone).ToArray();
    public void Update(UserAccount user) { var e = db.Users.FirstOrDefault(x => x.NormalizedEmail == N(user.Email)); if (e is null) db.Users.Add(ToEntity(user)); else { e.Email = user.Email; e.NormalizedEmail = N(user.Email); e.PasswordHash = user.PasswordHash; e.DisplayName = user.DisplayName; e.ConfirmationToken = user.ConfirmationToken; e.EmailConfirmed = user.EmailConfirmed; e.ProfileStatus = user.Profile.Status; e.DailySendLimit = user.Profile.DailySendLimit; e.TotalSendLimit = user.Profile.TotalSendLimit; e.PremoderationRequired = user.Profile.PremoderationRequired; e.UpdatedAt = DateTimeOffset.UtcNow; } db.SaveChanges(); SavePhone(user.Email, user.Phone); }
    public void Remove(string email) { var e = db.Users.FirstOrDefault(x => x.NormalizedEmail == N(email)); if (e is null) return; db.Users.Remove(e); db.SaveChanges(); }
    private static UserEntity ToEntity(UserAccount u) => new() { Email = u.Email, NormalizedEmail = N(u.Email), PasswordHash = u.PasswordHash, DisplayName = u.DisplayName, ConfirmationToken = u.ConfirmationToken, EmailConfirmed = u.EmailConfirmed, ProfileStatus = u.Profile.Status, DailySendLimit = u.Profile.DailySendLimit, TotalSendLimit = u.Profile.TotalSendLimit, PremoderationRequired = u.Profile.PremoderationRequired, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    private static UserAccount ToDomain(UserEntity e) => new(e.Email, e.PasswordHash, e.DisplayName, e.ConfirmationToken, e.EmailConfirmed, new ClientProfile(e.ProfileStatus, e.DailySendLimit, e.TotalSendLimit, e.PremoderationRequired), new());
    private UserAccount WithPhone(UserAccount user) => user with { Phone = ReadPhone(user.Email) };
    private void SavePhone(string email, string phone) { try { EnsurePhoneColumn(); db.Database.ExecuteSqlInterpolated($"UPDATE users SET \"Phone\" = {phone} WHERE \"NormalizedEmail\" = {N(email)};"); } catch { } }
    private string ReadPhone(string email)
    {
        try
        {
            EnsurePhoneColumn();
            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose) connection.Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT \"Phone\" FROM users WHERE \"NormalizedEmail\" = @email LIMIT 1;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@email";
                parameter.Value = N(email);
                command.Parameters.Add(parameter);
                return command.ExecuteScalar()?.ToString() ?? string.Empty;
            }
            finally
            {
                if (shouldClose) connection.Close();
            }
        }
        catch
        {
            return string.Empty;
        }
    }
    private void EnsurePhoneColumn() => db.Database.ExecuteSqlRaw("ALTER TABLE users ADD COLUMN IF NOT EXISTS \"Phone\" character varying(40) NOT NULL DEFAULT ''; ");
    private static string NormalizePhone(string phone) { var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray()); if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..]; return digits; }
    private static string N(string v) => v.Trim().ToLowerInvariant();
}

public sealed class EfGlobalSuppressionRepository(PismoletDbContext db) : IGlobalSuppressionRepository
{
    public bool IsSuppressed(string normalizedEmail) => db.GlobalSuppressions.Any(x => x.EmailNormalized == N(normalizedEmail));
    public IReadOnlySet<string> GetSuppressedSet(IEnumerable<string> normalizedEmails) { var emails = normalizedEmails.Select(N).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); return db.GlobalSuppressions.AsNoTracking().Where(x => emails.Contains(x.EmailNormalized)).Select(x => x.EmailNormalized).ToHashSet(StringComparer.OrdinalIgnoreCase); }
    public GlobalSuppression? GetByEmail(string normalizedEmail) => db.GlobalSuppressions.AsNoTracking().FirstOrDefault(x => x.EmailNormalized == N(normalizedEmail)) is { } x ? ToDomain(x) : null;
    public IReadOnlyCollection<GlobalSuppression> ListAll() => db.GlobalSuppressions.AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(x => ToDomain(x)).ToArray();
    public GlobalSuppression AddOrGet(GlobalSuppression suppression) { var email = N(suppression.EmailNormalized); var existing = db.GlobalSuppressions.AsNoTracking().FirstOrDefault(x => x.EmailNormalized == email); if (existing is not null) return ToDomain(existing); var entity = ToEntity(suppression with { EmailNormalized = email }); db.GlobalSuppressions.Add(entity); db.SaveChanges(); return ToDomain(entity); }
    public void Add(string normalizedEmail) { var email = N(normalizedEmail); if (!string.IsNullOrWhiteSpace(email)) AddOrGet(new GlobalSuppression(Guid.NewGuid(), email, string.Empty, GlobalSuppressionSource.Admin, null, null, DateTimeOffset.UtcNow, null, null)); }
    private static GlobalSuppressionEntity ToEntity(GlobalSuppression x) => new() { Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id, EmailNormalized = N(x.EmailNormalized), EmailHash = x.EmailHash, Source = x.Source.ToString(), SourceMailingId = x.SourceMailingId, SourceRecipientKey = x.SourceRecipientKey, CreatedAt = x.CreatedAt.ToUniversalTime(), CreatedIpHash = x.CreatedIpHash, UserAgentHash = x.UserAgentHash };
    private static GlobalSuppression ToDomain(GlobalSuppressionEntity x) => new(x.Id, x.EmailNormalized, x.EmailHash, Enum.Parse<GlobalSuppressionSource>(x.Source), x.SourceMailingId, x.SourceRecipientKey, x.CreatedAt, x.CreatedIpHash, x.UserAgentHash);
    private static string N(string v) => v.Trim().ToLowerInvariant();
}

public sealed class EfMailingRepository(PismoletDbContext db) : IMailingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool TryAdd(Mailing m)
    {
        if (db.Mailings.Any(x => x.Id == m.Id)) return false;
        db.Mailings.Add(ToEntity(m));
        SaveOwned(m);
        db.SaveChanges();
        return true;
    }

    public Mailing? Get(Guid id) => db.Mailings.AsNoTracking().FirstOrDefault(x => x.Id == id) is { } x ? ToDomain(x) : null;
    public Mailing? GetForOwner(Guid id, string ownerEmail) => db.Mailings.AsNoTracking().FirstOrDefault(x => x.Id == id && x.OwnerEmail == N(ownerEmail)) is { } x ? ToDomain(x) : null;
    public IReadOnlyCollection<Mailing> ListAll() => db.Mailings.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToArray().Select(ToDomain).ToArray();
    public IReadOnlyCollection<Mailing> ListForOwner(string ownerEmail) => db.Mailings.AsNoTracking().Where(x => x.OwnerEmail == N(ownerEmail)).OrderByDescending(x => x.CreatedAt).ToArray().Select(ToDomain).ToArray();
    public IReadOnlyDictionary<string, int> CountByOwners(IEnumerable<string> ownerEmails) { var owners = ownerEmails.Select(N).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); return owners.ToDictionary(x => x, x => db.Mailings.Count(m => m.OwnerEmail == x), StringComparer.OrdinalIgnoreCase); }

    public void Update(Mailing m)
    {
        var e = db.Mailings.FirstOrDefault(x => x.Id == m.Id);
        if (e is null)
        {
            db.Mailings.Add(ToEntity(m));
        }
        else
        {
            e.OwnerEmail = N(m.OwnerEmail);
            e.Subject = m.Subject;
            e.StatusRu = m.StatusRu;
            e.PublicId = m.PublicId;
            e.CreatedAt = m.CreatedAt.ToUniversalTime();
        }

        ClearOwned(m.Id);
        SaveOwned(m);
        db.SaveChanges();
    }

    public int RemoveForOwner(string ownerEmail)
    {
        var normalized = N(ownerEmail);
        var ids = db.Mailings.Where(x => x.OwnerEmail == normalized).Select(x => x.Id).ToArray();
        foreach (var id in ids) ClearOwned(id);
        db.Mailings.RemoveRange(db.Mailings.Where(x => x.OwnerEmail == normalized));
        db.SaveChanges();
        return ids.Length;
    }

    private MailingEntity ToEntity(Mailing m) => new() { Id = m.Id, OwnerEmail = N(m.OwnerEmail), Subject = m.Subject, StatusRu = m.StatusRu, PublicId = m.PublicId, CreatedAt = m.CreatedAt.ToUniversalTime() };

    private void ClearOwned(Guid id)
    {
        var batchIds = db.ImportBatches.Where(x => x.MailingId == id).Select(x => x.Id).ToArray();
        db.ImportIssues.RemoveRange(db.ImportIssues.Where(x => batchIds.Contains(x.ImportBatchId)));
        db.Recipients.RemoveRange(db.Recipients.Where(x => x.MailingId == id));
        db.ImportBatches.RemoveRange(db.ImportBatches.Where(x => x.MailingId == id));
        db.MailingDeclarations.RemoveRange(db.MailingDeclarations.Where(x => x.MailingId == id));
        db.MailingMessageDrafts.RemoveRange(db.MailingMessageDrafts.Where(x => x.MailingId == id));
    }

    private void SaveOwned(Mailing m)
    {
        foreach (var b in m.ImportBatches)
        {
            db.ImportBatches.Add(new ImportBatchEntity
            {
                Id = b.Id == Guid.Empty ? Guid.NewGuid() : b.Id,
                MailingId = m.Id,
                FileName = b.FileName,
                SourceFormat = b.SourceFormat.ToString(),
                CreatedAt = b.CreatedAt.ToUniversalTime(),
                TotalRows = b.TotalRows,
                Accepted = b.Accepted,
                Duplicates = b.Duplicates,
                Invalid = b.Invalid,
                GloballySuppressed = b.GloballySuppressed,
                ClientSuppressed = b.ClientSuppressed,
                Status = b.Status.ToString()
            });

            db.ImportIssues.AddRange(b.Issues.Select(issue => new ImportIssueEntity
            {
                Id = Guid.NewGuid(),
                ImportBatchId = b.Id,
                RowNumber = issue.RowNumber,
                Email = issue.Email,
                Message = issue.Message
            }));
        }

        db.Recipients.AddRange(m.Recipients.Select(r => new RecipientEntity
        {
            Id = Guid.NewGuid(),
            MailingId = m.Id,
            RowNumber = r.RowNumber,
            SourceEmail = r.SourceEmail,
            NormalizedEmail = N(r.Email),
            Status = r.Status.ToString(),
            ExclusionReason = r.ExclusionReason,
            ImportBatchId = r.ImportBatchId
        }));

        if (m.Declaration is { } d)
        {
            db.MailingDeclarations.Add(new MailingDeclarationEntity
            {
                MailingId = m.Id,
                ImportBatchId = d.ImportBatchId,
                UserEmail = N(d.UserEmail),
                BaseSource = d.BaseSource.ToString(),
                IsBaseLegalityConfirmed = d.IsBaseLegalityConfirmed,
                IsAdvertisingConsentConfirmed = d.IsAdvertisingConsentConfirmed,
                DeclarationVersion = d.DeclarationVersion,
                CreatedAt = d.CreatedAt.ToUniversalTime(),
                Ip = d.Ip,
                UserAgent = d.UserAgent
            });
        }

        if (m.MessageDraft is { } dr)
        {
            db.MailingMessageDrafts.Add(new MailingMessageDraftEntity
            {
                MailingId = m.Id,
                SenderName = dr.SenderName,
                Subject = dr.Subject,
                Body = dr.Body,
                MessageType = dr.MessageType.ToString(),
                BodyFormat = dr.BodyFormat.ToString(),
                UpdatedAt = dr.UpdatedAt.ToUniversalTime(),
                AttachmentsJson = SerializeAttachments(dr.Attachments)
            });
        }
    }

    private Mailing ToDomain(MailingEntity e)
    {
        var issueEntities = db.ImportIssues.AsNoTracking()
            .Join(db.ImportBatches.AsNoTracking().Where(x => x.MailingId == e.Id), issue => issue.ImportBatchId, batch => batch.Id, (issue, batch) => issue)
            .ToArray();
        var issuesByBatch = issueEntities
            .GroupBy(x => x.ImportBatchId)
            .ToDictionary(x => x.Key, x => (IReadOnlyCollection<RecipientImportIssue>)x.OrderBy(issue => issue.RowNumber).Select(issue => new RecipientImportIssue(issue.RowNumber, issue.Email, issue.Message)).ToArray());
        var batches = db.ImportBatches.AsNoTracking()
            .Where(x => x.MailingId == e.Id)
            .ToArray()
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ImportBatch(x.Id, x.MailingId, x.FileName, Enum.Parse<ImportSourceFormat>(x.SourceFormat), x.CreatedAt, x.TotalRows, x.Accepted, x.Duplicates, x.Invalid, x.GloballySuppressed, Enum.Parse<ImportBatchStatus>(x.Status), issuesByBatch.GetValueOrDefault(x.Id) ?? Array.Empty<RecipientImportIssue>(), x.ClientSuppressed))
            .ToList();
        var recipients = db.Recipients.AsNoTracking()
            .Where(x => x.MailingId == e.Id)
            .OrderBy(x => x.RowNumber)
            .ToArray()
            .Select(x => new Recipient(x.SourceEmail, x.NormalizedEmail, Enum.Parse<RecipientStatus>(x.Status), x.ExclusionReason, x.ImportBatchId, x.RowNumber))
            .ToList();
        var declaration = db.MailingDeclarations.AsNoTracking().FirstOrDefault(x => x.MailingId == e.Id);
        var draft = db.MailingMessageDrafts.AsNoTracking().FirstOrDefault(x => x.MailingId == e.Id);
        var last = batches.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return new Mailing(e.Subject, e.StatusRu)
        {
            Id = e.Id,
            OwnerEmail = e.OwnerEmail,
            CreatedAt = e.CreatedAt,
            PublicId = e.PublicId,
            ImportBatches = batches,
            LastImportBatch = last,
            LastImportStats = last?.ToStats() ?? ImportStats.Empty,
            Recipients = recipients,
            Declaration = declaration is null ? null : new MailingDeclaration(declaration.MailingId, declaration.UserEmail, Enum.Parse<BaseSource>(declaration.BaseSource), declaration.IsBaseLegalityConfirmed, declaration.IsAdvertisingConsentConfirmed, declaration.DeclarationVersion, declaration.CreatedAt, declaration.Ip, declaration.UserAgent) { ImportBatchId = declaration.ImportBatchId },
            MessageDraft = draft is null ? null : new MailingMessageDraft(draft.SenderName, draft.Subject, draft.Body, Enum.Parse<MessageType>(draft.MessageType), draft.UpdatedAt, DeserializeAttachments(draft.AttachmentsJson), ParseBodyFormat(draft.BodyFormat, draft.Body))
        };
    }

    private static MessageBodyFormat ParseBodyFormat(string? value, string body)
    {
        if (Enum.TryParse<MessageBodyFormat>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return MessageBodyFormatDetector.InferFromBody(body);
    }

    private static string SerializeAttachments(IReadOnlyCollection<MailingAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return "[]";
        }

        var dto = attachments
            .Select(x => new MailingAttachmentDto(x.FileName, x.ContentType, x.Content))
            .ToArray();
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static IReadOnlyCollection<MailingAttachment> DeserializeAttachments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<MailingAttachment>();
        }

        try
        {
            var dto = JsonSerializer.Deserialize<MailingAttachmentDto[]>(json, JsonOptions) ?? Array.Empty<MailingAttachmentDto>();
            return dto
                .Where(x => !string.IsNullOrWhiteSpace(x.FileName) && x.Content.Length > 0)
                .Select(x => MailingAttachment.Create(x.FileName, x.ContentType, x.Content))
                .ToArray();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            return Array.Empty<MailingAttachment>();
        }
    }

    private static string N(string v) => v.Trim().ToLowerInvariant();

    private sealed record MailingAttachmentDto(string FileName, string ContentType, byte[] Content);
}
