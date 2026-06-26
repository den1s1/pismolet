using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryAdminMvpSettingsRepository : IAdminMvpSettingsRepository
{
    private AdminMvpSettings _settings = AdminMvpSettings.Default;

    public AdminMvpSettings Get() => _settings;

    public void Save(AdminMvpSettings settings)
    {
        settings.EnsureValid();
        _settings = settings;
    }
}

// Sprint 10 MVP persistence bridge.
// До отдельной EF-таблицы settings Postgres-режим использует singleton repository.
public sealed class RuntimeAdminMvpSettingsRepository : IAdminMvpSettingsRepository
{
    private AdminMvpSettings _settings = AdminMvpSettings.Default;

    public AdminMvpSettings Get() => _settings;

    public void Save(AdminMvpSettings settings)
    {
        settings.EnsureValid();
        _settings = settings;
    }
}
