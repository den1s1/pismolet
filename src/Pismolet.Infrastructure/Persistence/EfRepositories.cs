using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfUserRepository(PismoletDbContext db) : IUserRepository
{
    public bool Exists(string email) => db.Users.Any(x => x.NormalizedEmail == Normalize(email));

    public bool TryAdd(UserAccount user)
    {
        if (Exists(user.Email))
        {
            return false;
        }

        db.Users.Add(ToEntity(user));
        db.SaveChanges();
        return true;
    }

    public UserAccount? GetByEmail(string email) => db.Users
        .AsNoTracking()
        .FirstOrDefault(x => x.NormalizedEmail == Normalize(email)) is { } entity
            ? ToDomain(entity)
            : null;

    public UserAccount? FindByConfirmationToken(string token) => db.Users
        .AsNoTracking()
        .FirstOrDefault(x => x.ConfirmationToken == token) is { } entity
            ? ToDomain(entity)
            : null;

    public IReadOnlyCollection<UserAccount> ListAll() => db.Users
        .AsNoTracking()
        .OrderBy(x => x.NormalizedEmail)
        .ToArray()
        .Select(ToDomain)
        .ToArray();

    public void Update(UserAccount user)
    {
        var normalized = Normalize(user.Email);
        var entity = db.Users.FirstOrDefault(x => x.NormalizedEmail == normalized);
        if (entity is null)
        {
            db.Users.Add(ToEntity(user));
        }
        else
        {
            entity.Email = user.Email;
            entity.NormalizedEmail = normalized;
            entity.PasswordHash = user.PasswordHash;
            entity.DisplayName = user.DisplayName;
            entity.ConfirmationToken = user.ConfirmationToken;
            entity.EmailConfirmed = user.EmailConfirmed;
            entity.ProfileStatus = user.Profile.Status;
            entity.DailySendLimit = user.Profile.DailySendLimit;
            entity.TotalSendLimit = user.Profile.TotalSendLimit;
            entity.PremoderationRequired = user.Profile.PremoderationRequired;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        db.SaveChanges();
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static UserEntity ToEntity(UserAccount user) => new()
    {
        Email = user.Email,
        NormalizedEmail = Normalize(user.Email),
        PasswordHash = user.PasswordHash,
        DisplayName = user.DisplayName,
        ConfirmationToken = user.ConfirmationToken,
        EmailConfirmed = user.EmailConfirmed,
        ProfileStatus = user.Profile.Status,
        DailySendLimit = user.Profile.DailySendLimit,
        TotalSendLimit = user.Profile.TotalSendLimit,
        PremoderationRequired = user.Profile.PremoderationRequired,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static UserAccount ToDomain(UserEntity entity) => new(
        entity.Email,
        entity.PasswordHash,
        entity.DisplayName,
        entity.ConfirmationToken,
        entity.EmailConfirmed,
        new ClientProfile(entity.ProfileStatus, entity.DailySendLimit, entity.TotalSendLimit, entity.PremoderationRequired),
        new List<Mailing>());
}

public sealed class EfGlobalSuppressionRepository(PismoletDbContext db) : IGlobalSuppressionRepository
{
    public bool IsSuppressed(string normalizedEmail) => db.GlobalSuppressions.Any(x => x.EmailNormalized == Normalize(normalizedEmail));

    public IReadOnlySet<string> GetSuppressedSet(IEnumerable<string> normalizedEmails)
    {
        var emails = normalizedEmails
            .Select(Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (emails.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return db.GlobalSuppressions
            .AsNoTracking()
            .Where(x => emails.Contains(x.EmailNormalized))
            .Select(x => x.EmailNormalized)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public GlobalSuppression? GetByEmail(string normalizedEmail) => db.GlobalSuppressions
        .AsNoTracking()
        .FirstOrDefault(x => x.EmailNormalized == Normalize(normalizedEmail)) is { } entity
            ? ToDomain(entity)
            : null;

    public GlobalSuppression AddOrGet(GlobalSuppression suppression)
    {
        var normalized = Normalize(suppression.EmailNormalized);
        var existing = db.GlobalSuppressions.AsNoTracking().FirstOrDefault(x => x.EmailNormalized == normalized);
        if (existing is not null)
        {
            return ToDomain(existing);
        }

        var entity = ToEntity(suppression with { EmailNormalized = normalized });
        db.GlobalSuppressions.Add(entity);
        try
        {
            db.SaveChanges();
            return ToDomain(entity);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var current = db.GlobalSuppressions.AsNoTracking().FirstOrDefault(x => x.EmailNormalized == normalized);
            if (current is not null)
            {
                return ToDomain(current);
            }

            throw;
        }
    }

    public void Add(string normalizedEmail)
    {
        var email = Normalize(normalizedEmail);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        AddOrGet(new GlobalSuppression(
            Guid.NewGuid(),
            email,
            string.Empty,
            GlobalSuppressionSource.Admin,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null));
    }

    private static GlobalSuppressionEntity ToEntity(GlobalSuppression suppression) => new()
    {
        Id = suppression.Id == Guid.Empty ? Guid.NewGuid() : suppression.Id,
        EmailNormalized = Normalize(suppression.EmailNormalized),
        EmailHash = suppression.EmailHash,
        Source = suppression.Source.ToString(),
        SourceMailingId = suppression.SourceMailingId,
        SourceRecipientKey = suppression.SourceRecipientKey,
        CreatedAt = suppression.CreatedAt.ToUniversalTime(),
        CreatedIpHash = suppression.CreatedIpHash,
        UserAgentHash = suppression.UserAgentHash
    };

    private static GlobalSuppression ToDomain(GlobalSuppressionEntity entity) => new(
        entity.Id,
        entity.EmailNormalized,
        entity.EmailHash,
        Enum.Parse<GlobalSuppressionSource>(entity.Source),
        entity.SourceMailingId,
        entity.SourceRecipientKey,
        entity.CreatedAt,
        entity.CreatedIpHash,
        entity.UserAgentHash);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

public sealed class EfMailingRepository(PismoletDbContext db) : IMailingRepository
{
    public bool TryAdd(Mailing mailing)
    {
        if (db.Mailings.Any(x => x.Id == mailing.Id))
        {
            return false;
        }

        AddGraph(mailing);
        db.SaveChanges();
        return true;
    }

    public Mailing? Get(Guid id) => db.Mailings.AsNoTracking().FirstOrDefault(x => x.Id == id) is { } entity
        ? ToDomain(entity)
        : null;

    public Mailing? GetForOwner(Guid id, string ownerEmail)
    {
        var normalized = Normalize(ownerEmail);
        var entity = db.Mailings.AsNoTracking().FirstOrDefault(x => x.Id == id && x.OwnerEmail == normalized);
        return entity is null ? null : ToDomain(entity);
    }

    public IReadOnlyCollection<Mailing> ListForOwner(string ownerEmail)
    {
        var normalized = Normalize(ownerEmail);
        return db.Mailings
            .AsNoTracking()
            .Where(x => x.OwnerEmail == normalized)
            .OrderByDescending(x => x.CreatedAt)
            .ToArray()
            .Select(ToDomain)
            .ToArray();
    }

    public void Update(Mailing mailing)
    {
        var entity = db.Mailings.FirstOrDefault(x => x.Id == mailing.Id);
        if (entity is null)
        {
            AddGraph(mailing);
            db.SaveChanges();
            return;
        }

        entity.OwnerEmail = Normalize(mailing.OwnerEmail);
        entity.Subject = mailing.Subject;
        entity.StatusRu = mailing.StatusRu;
        entity.PublicId = mailing.PublicId;
        entity.CreatedAt = mailing.CreatedAt.ToUniversalTime();

        var batchIds = db.ImportBatches.Where(x => x.MailingId == mailing.Id).Select(x => x.Id).ToArray();
        db.ImportIssues.RemoveRange(db.ImportIssues.Where(x => batchIds.Contains(x.ImportBatchId)));
        db.Recipients.RemoveRange(db.Recipients.Where(x => x.MailingId == mailing.Id));
        db.ImportBatches.RemoveRange(db.ImportBatches.Where(x => x.MailingId == mailing.Id));
        db.MailingDeclarations.RemoveRange(db.MailingDeclarations.Where(x => x.MailingId == mailing.Id));
        db.MailingMessageDrafts.RemoveRange(db.MailingMessageDrafts.Where(x => x.MailingId == mailing.Id));

        AddOwnedGraph(mailing);
        db.SaveChanges();
    }

    private void AddGraph(Mailing mailing)
    {
        db.Mailings.Add(new MailingEntity
        {
            Id = mailing.Id,
            OwnerEmail = Normalize(mailing.OwnerEmail),
            Subject = mailing.Subject,
            StatusRu = mailing.StatusRu,
            PublicId = mailing.PublicId,
            CreatedAt = mailing.CreatedAt.ToUniversalTime()
        });

        AddOwnedGraph(mailing);
    }

    private void AddOwnedGraph(Mailing mailing)
    {
        foreach (var batch in mailing.ImportBatches)
        {
            var batchId = batch.Id == Guid.Empty ? Guid.NewGuid() : batch.Id;
            db.ImportBatches.Add(new ImportBatchEntity
            {
                Id = batchId,
                MailingId = mailing.Id,
                FileName = batch.FileName,
                SourceFormat = batch.SourceFormat.ToString(),
                CreatedAt = batch.CreatedAt.ToUniversalTime(),
                TotalRows = batch.TotalRows,
                Accepted = batch.Accepted,
                Duplicates = batch.Duplicates,
                Invalid = batch.Invalid,
                GloballySuppressed = batch.GloballySuppressed,
                Status = batch.Status.ToString()
            });

            foreach (var issue in batch.Issues)
            {
                db.ImportIssues.Add(new ImportIssueEntity
                {
                    Id = Guid.NewGuid(),
                    ImportBatchId = batchId,
                    RowNumber = issue.RowNumber,
                    Email = issue.Email,
                    Message = issue.Message
                });
            }
        }

        db.Recipients.AddRange(mailing.Recipients.Select(recipient => new RecipientEntity
        {
            Id = Guid.NewGuid(),
            MailingId = mailing.Id,
            SourceEmail = recipient.SourceEmail,
            NormalizedEmail = Normalize(recipient.Email),
            Status = recipient.Status.ToString(),
            ExclusionReason = recipient.ExclusionReason,
            ImportBatchId = recipient.ImportBatchId
        }));

        if (mailing.Declaration is { } declaration)
        {
            db.MailingDeclarations.Add(new MailingDeclarationEntity
            {
                MailingId = mailing.Id,
                ImportBatchId = declaration.ImportBatchId,
                UserEmail = Normalize(declaration.UserEmail),
                BaseSource = declaration.BaseSource.ToString(),
                IsBaseLegalityConfirmed = declaration.IsBaseLegalityConfirmed,
                IsAdvertisingConsentConfirmed = declaration.IsAdvertisingConsentConfirmed,
                DeclarationVersion = declaration.DeclarationVersion,
                CreatedAt = declaration.CreatedAt.ToUniversalTime(),
                Ip = declaration.Ip,
                UserAgent = declaration.UserAgent
            });
        }

        if (mailing.MessageDraft is { } draft)
        {
            db.MailingMessageDrafts.Add(new MailingMessageDraftEntity
            {
                MailingId = mailing.Id,
                SenderName = draft.SenderName,
                Subject = draft.Subject,
                Body = draft.Body,
                MessageType = draft.MessageType.ToString(),
                UpdatedAt = draft.UpdatedAt.ToUniversalTime()
            });
        }
    }

    private Mailing ToDomain(MailingEntity entity)
    {
        var batches = db.ImportBatches
            .AsNoTracking()
            .Where(x => x.MailingId == entity.Id)
            .OrderBy(x => x.CreatedAt)
            .ToArray()
            .Select(ToDomainBatch)
            .ToList();

        var recipients = db.Recipients
            .AsNoTracking()
            .Where(x => x.MailingId == entity.Id)
            .OrderBy(x => x.NormalizedEmail)
            .Select(ToDomainRecipient)
            .ToList();

        var declaration = db.MailingDeclarations
            .AsNoTracking()
            .FirstOrDefault(x => x.MailingId == entity.Id) is { } declarationEntity
                ? ToDomainDeclaration(declarationEntity)
                : null;

        var draft = db.MailingMessageDrafts
            .AsNoTracking()
            .FirstOrDefault(x => x.MailingId == entity.Id) is { } draftEntity
                ? ToDomainDraft(draftEntity)
                : null;

        var lastBatch = batches.OrderByDescending(x => x.CreatedAt).FirstOrDefault();

        return new Mailing(entity.Subject, entity.StatusRu)
        {
            Id = entity.Id,
            OwnerEmail = entity.OwnerEmail,
            CreatedAt = entity.CreatedAt,
            PublicId = entity.PublicId,
            ImportBatches = batches,
            LastImportBatch = lastBatch,
            LastImportStats = lastBatch?.ToStats() ?? ImportStats.Empty,
            Recipients = recipients,
            Declaration = declaration,
            MessageDraft = draft
        };
    }

    private ImportBatch ToDomainBatch(ImportBatchEntity entity)
    {
        var issues = db.ImportIssues
            .AsNoTracking()
            .Where(x => x.ImportBatchId == entity.Id)
            .OrderBy(x => x.RowNumber)
            .Select(x => new RecipientImportIssue(x.RowNumber, x.Email, x.Message))
            .ToArray();

        return new ImportBatch(
            entity.Id,
            entity.MailingId,
            entity.FileName,
            Enum.Parse<ImportSourceFormat>(entity.SourceFormat),
            entity.CreatedAt,
            entity.TotalRows,
            entity.Accepted,
            entity.Duplicates,
            entity.Invalid,
            entity.GloballySuppressed,
            Enum.Parse<ImportBatchStatus>(entity.Status),
            issues);
    }

    private static Recipient ToDomainRecipient(RecipientEntity entity) => new(
        entity.SourceEmail,
        entity.NormalizedEmail,
        Enum.Parse<RecipientStatus>(entity.Status),
        entity.ExclusionReason,
        entity.ImportBatchId);

    private static MailingDeclaration ToDomainDeclaration(MailingDeclarationEntity entity) => new(
        entity.MailingId,
        entity.UserEmail,
        Enum.Parse<BaseSource>(entity.BaseSource),
        entity.IsBaseLegalityConfirmed,
        entity.IsAdvertisingConsentConfirmed,
        entity.DeclarationVersion,
        entity.CreatedAt,
        entity.Ip,
        entity.UserAgent)
    {
        ImportBatchId = entity.ImportBatchId
    };

    private static MailingMessageDraft ToDomainDraft(MailingMessageDraftEntity entity) => new(
        entity.SenderName,
        entity.Subject,
        entity.Body,
        Enum.Parse<MessageType>(entity.MessageType),
        entity.UpdatedAt);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
