using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class ReplyAliasRepositoryTests
{
    [Fact]
    public void Client_alias_repository_keeps_alias_unique()
    {
        var repository = new InMemoryClientReplyAliasRepository();
        var first = ClientReplyAlias.Create("User@Mail.Ru", "user");

        repository.Save(first);

        Assert.True(repository.AliasExists("USER"));
        Assert.Equal("user@mail.ru", repository.GetByAlias("user")?.ClientId);
        Assert.Throws<InvalidOperationException>(() => repository.Save(ClientReplyAlias.Create("other@yandex.ru", "user")));
    }

    [Fact]
    public void Client_alias_repository_add_or_get_returns_existing_alias_for_client()
    {
        var repository = new InMemoryClientReplyAliasRepository();
        var first = ClientReplyAlias.Create("user@mail.ru", "user");
        var second = ClientReplyAlias.Create("user@mail.ru", "user-2");

        var saved = repository.AddOrGet(first);
        var existing = repository.AddOrGet(second);

        Assert.Equal(saved.Id, existing.Id);
        Assert.Equal("user", existing.Alias);
    }

    [Fact]
    public void Outbound_message_mapping_repository_saves_and_reads_by_message_id()
    {
        var repository = new InMemoryOutboundReplyMessageRepository();
        var mailingId = Guid.NewGuid();
        var sendEventId = Guid.NewGuid();
        var mapping = OutboundReplyMessageMapping.Create(
            "message-1@app.pismolet.ru",
            mailingId,
            sendEventId,
            "User@Mail.Ru",
            "Recipient@Example.Ru",
            "user");

        repository.Save(mapping);

        var saved = repository.GetByMessageId("<message-1@app.pismolet.ru>");
        Assert.NotNull(saved);
        Assert.Equal(mailingId, saved.MailingId);
        Assert.Equal(sendEventId, saved.SendEventId);
        Assert.Equal("user@mail.ru", saved.ClientId);
        Assert.Equal("recipient@example.ru", saved.RecipientEmailNormalized);
        Assert.Equal("user", saved.ReplyAlias);
    }
}
