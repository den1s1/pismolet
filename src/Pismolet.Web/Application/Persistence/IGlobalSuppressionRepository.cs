namespace Pismolet.Web.Application.Persistence;

public interface IGlobalSuppressionRepository
{
    bool IsSuppressed(string normalizedEmail);

    void Add(string normalizedEmail);
}
