using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfClientReplyAliasRepository(PismoletDbContext db) : IClientReplyAliasRepository
{
    public ClientReplyAlias? GetByClientId(string clientId) => QuerySingle(
        """
        SELECT "Id", "ClientId", "Alias", "CreatedAt", "UpdatedAt"
        FROM client_reply_aliases
        WHERE "ClientId" = @value
        LIMIT 1;
        """,
        Normalize(clientId));

    public ClientReplyAlias? GetByAlias(string alias) => QuerySingle(
        """
        SELECT "Id", "ClientId", "Alias", "CreatedAt", "UpdatedAt"
        FROM client_reply_aliases
        WHERE "Alias" = @value
        LIMIT 1;
        """,
        Normalize(alias));

    public bool AliasExists(string alias) => GetByAlias(alias) is not null;

    public ClientReplyAlias AddOrGet(ClientReplyAlias alias)
    {
        var normalizedClient = Normalize(alias.ClientId);
        if (GetByClientId(normalizedClient) is { } existing)
        {
            return existing;
        }

        Save(alias with
        {
            ClientId = normalizedClient,
            Alias = Normalize(alias.Alias)
        });

        return GetByClientId(normalizedClient) ?? alias;
    }

    public void Save(ClientReplyAlias alias)
    {
        var now = DateTimeOffset.UtcNow;
        var id = alias.Id == Guid.Empty ? Guid.NewGuid() : alias.Id;
        Execute(
            """
            INSERT INTO client_reply_aliases ("Id", "ClientId", "Alias", "CreatedAt", "UpdatedAt")
            VALUES (@id, @clientId, @alias, @createdAt, @updatedAt)
            ON CONFLICT ("ClientId") DO UPDATE
            SET "Alias" = EXCLUDED."Alias", "UpdatedAt" = EXCLUDED."UpdatedAt";
            """,
            new Dictionary<string, object?>
            {
                ["@id"] = id,
                ["@clientId"] = Normalize(alias.ClientId),
                ["@alias"] = Normalize(alias.Alias),
                ["@createdAt"] = alias.CreatedAt == default ? now : alias.CreatedAt.ToUniversalTime(),
                ["@updatedAt"] = alias.UpdatedAt == default ? now : alias.UpdatedAt.ToUniversalTime()
            });
    }

    private ClientReplyAlias? QuerySingle(string sql, string value)
    {
        using var command = CreateCommand(sql, new Dictionary<string, object?> { ["@value"] = value });
        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        return reader.Read() ? ReadAlias(reader) : null;
    }

    private void Execute(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using var command = CreateCommand(sql, parameters);
        command.ExecuteNonQuery();
    }

    private DbCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var item in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = item.Key;
            parameter.Value = item.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static ClientReplyAlias ReadAlias(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        ReadDate(reader.GetValue(3)),
        ReadDate(reader.GetValue(4)));

    private static DateTimeOffset ReadDate(object value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty).ToUniversalTime()
    };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed class EfOutboundReplyMessageRepository(PismoletDbContext db) : IOutboundReplyMessageRepository
{
    public OutboundReplyMessageMapping? GetByMessageId(string messageId)
    {
        var normalized = OutboundReplyMessageMapping.NormalizeMessageId(messageId);
        using var command = CreateCommand(
            """
            SELECT "Id", "MessageId", "MailingId", "SendEventId", "ClientId", "RecipientEmailNormalized", "ReplyAlias", "CreatedAt"
            FROM outbound_reply_message_mappings
            WHERE "MessageId" = @messageId
            LIMIT 1;
            """,
            new Dictionary<string, object?> { ["@messageId"] = normalized });
        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        return reader.Read() ? ReadMapping(reader) : null;
    }

    public IReadOnlyCollection<OutboundReplyMessageMapping> FindByMessageIds(IEnumerable<string> messageIds)
    {
        var result = new List<OutboundReplyMessageMapping>();
        foreach (var messageId in messageIds.Select(OutboundReplyMessageMapping.NormalizeMessageId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (GetByMessageId(messageId) is { } mapping)
            {
                result.Add(mapping);
            }
        }

        return result;
    }

    public void Save(OutboundReplyMessageMapping mapping)
    {
        var id = mapping.Id == Guid.Empty ? Guid.NewGuid() : mapping.Id;
        Execute(
            """
            INSERT INTO outbound_reply_message_mappings
                ("Id", "MessageId", "MailingId", "SendEventId", "ClientId", "RecipientEmailNormalized", "ReplyAlias", "CreatedAt")
            VALUES
                (@id, @messageId, @mailingId, @sendEventId, @clientId, @recipientEmail, @replyAlias, @createdAt)
            ON CONFLICT ("MessageId") DO UPDATE
            SET "MailingId" = EXCLUDED."MailingId",
                "SendEventId" = EXCLUDED."SendEventId",
                "ClientId" = EXCLUDED."ClientId",
                "RecipientEmailNormalized" = EXCLUDED."RecipientEmailNormalized",
                "ReplyAlias" = EXCLUDED."ReplyAlias";
            """,
            new Dictionary<string, object?>
            {
                ["@id"] = id,
                ["@messageId"] = OutboundReplyMessageMapping.NormalizeMessageId(mapping.MessageId),
                ["@mailingId"] = mapping.MailingId,
                ["@sendEventId"] = mapping.SendEventId,
                ["@clientId"] = Normalize(mapping.ClientId),
                ["@recipientEmail"] = Normalize(mapping.RecipientEmailNormalized),
                ["@replyAlias"] = Normalize(mapping.ReplyAlias),
                ["@createdAt"] = mapping.CreatedAt == default ? DateTimeOffset.UtcNow : mapping.CreatedAt.ToUniversalTime()
            });
    }

    private void Execute(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using var command = CreateCommand(sql, parameters);
        command.ExecuteNonQuery();
    }

    private DbCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var item in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = item.Key;
            parameter.Value = item.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static OutboundReplyMessageMapping ReadMapping(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetGuid(2),
        reader.GetGuid(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        ReadDate(reader.GetValue(7)));

    private static DateTimeOffset ReadDate(object value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty).ToUniversalTime()
    };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
