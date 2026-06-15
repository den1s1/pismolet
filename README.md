# pismolet

MVP web-сервиса простых email-рассылок «Письмолёт».

## Development / Sprint 1

Добавлен первый вертикальный .NET-срез:

- регистрация пользователя;
- вход и выход;
- подтверждение email через dev/fake mailer;
- создание профиля клиента с лимитами;
- защищённый личный кабинет со списком рассылок;
- audit log ключевых действий.

Локальный запуск:

```bash
dotnet run --project src/Pismolet.Web/Pismolet.Web.csproj
```

После запуска:

- главная страница: `/`;
- регистрация: `/account/register`;
- вход: `/account/login`;
- личный кабинет: `/dashboard`;
- fake mailer: `/dev/fake-mailer`;
- health-check: `/health`.
