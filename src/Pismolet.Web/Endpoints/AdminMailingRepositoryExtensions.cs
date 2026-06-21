using System.Collections;
using System.Reflection;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

internal static class AdminMailingRepositoryExtensions
{
    public static IReadOnlyCollection<Mailing> ListAll(this IMailingRepository repository)
    {
        var publicListAll = repository.GetType().GetMethod("ListAll", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
        if (publicListAll?.Invoke(repository, null) is IReadOnlyCollection<Mailing> publicResult)
        {
            return publicResult;
        }

        return TryListEfMailings(repository);
    }

    private static IReadOnlyCollection<Mailing> TryListEfMailings(IMailingRepository repository)
    {
        var dbField = repository.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(field => field.FieldType.Name == "PismoletDbContext");
        var db = dbField?.GetValue(repository);
        if (db is null)
        {
            return Array.Empty<Mailing>();
        }

        var mailingsValue = db.GetType().GetProperty("Mailings")?.GetValue(db);
        if (mailingsValue is not IEnumerable mailingEntities)
        {
            return Array.Empty<Mailing>();
        }

        var toDomain = repository.GetType().GetMethod("ToDomain", BindingFlags.Instance | BindingFlags.NonPublic);
        if (toDomain is null)
        {
            return Array.Empty<Mailing>();
        }

        return mailingEntities.Cast<object>()
            .OrderByDescending(ReadCreatedAt)
            .Select(entity => toDomain.Invoke(repository, [entity]))
            .OfType<Mailing>()
            .ToArray();
    }

    private static DateTimeOffset ReadCreatedAt(object entity)
    {
        var value = entity.GetType().GetProperty("CreatedAt")?.GetValue(entity);
        return value is DateTimeOffset createdAt ? createdAt : DateTimeOffset.MinValue;
    }
}
