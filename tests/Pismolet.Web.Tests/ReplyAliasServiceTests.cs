using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class ReplyAliasServiceTests
{
    [Theory]
    [InlineData("User.Name_1@mail.ru", "user.name_1")]
    [InlineData("user+sales@mail.ru", "user-sales")]
    [InlineData("..user---sales__@mail.ru", "user-sales_")]
    [InlineData("u@mail.ru", null)]
    [InlineData("support@mail.ru", null)]
    public void Build_candidate_normalizes_local_part(string clientId, string? expected)
    {
        var service = CreateService();

        var alias = service.BuildCandidate(clientId);

        if (expected is null)
        {
            Assert.StartsWith("client-", alias);
            Assert.InRange(alias.Length, 10, ClientReplyAliasService.MaxAliasLength);
        }
        else
        {
            Assert.Equal(expected, alias);
        }
    }

    [Fact]
    public void Get_or_create_keeps_alias_stable_for_client()
    {
        var repository = new InMemoryClientReplyAliasRepository();
        var service = CreateService(repository);

        var first = service.GetOrCreate("User@Mail.Ru");
        var second = service.GetOrCreate("user@mail.ru");

        Assert.Equal("user", first.Alias);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Get_or_create_adds_numeric_suffix_on_collision()
    {
        var repository = new InMemoryClientReplyAliasRepository();
        var service = CreateService(repository);

        var first = service.GetOrCreate("user@mail.ru");
        var second = service.GetOrCreate("user@yandex.ru");
        var third = service.GetOrCreate("user@example.ru");

        Assert.Equal("user", first.Alias);
        Assert.Equal("user-2", second.Alias);
        Assert.Equal("user-3", third.Alias);
    }

    [Fact]
    public void Get_or_create_uses_fallback_for_reserved_alias()
    {
        var service = CreateService();

        var alias = service.GetOrCreate("admin@mail.ru");

        Assert.StartsWith("client-", alias.Alias);
    }

    private static ClientReplyAliasService CreateService(InMemoryClientReplyAliasRepository? repository = null) => new(
        repository ?? new InMemoryClientReplyAliasRepository(),
        new EmailNormalizer());
}
