# Деплой `public_html` на обычный хостинг

Документ описывает схему, при которой один GitHub-репозиторий обслуживает два окружения:

- `pismolet.ru` — публичный статический сайт из папки `public_html/`;
- `app.pismolet.ru` — приложение на VDS/VPS, которое разворачивается отдельно.

## Принцип

Папка `public_html/` в репозитории считается корнем публичного сайта `pismolet.ru`.

При деплое на хостинг копируется **содержимое** папки `public_html/`, а не сама папка.

Пример:

```text
repo/public_html/index.htm      -> pismolet.ru/index.htm
repo/public_html/assets/logo.svg -> pismolet.ru/assets/logo.svg
```

## Workflow

Деплой выполняет GitHub Actions workflow:

```text
.github/workflows/deploy-public-html.yml
```

Он запускается:

- при push в ветку `Development`, если изменились файлы `public_html/**`;
- вручную через `workflow_dispatch`.

## Требуемые GitHub Secrets

В репозитории нужно добавить секреты:

```text
PUBLIC_HTML_SSH_HOST
PUBLIC_HTML_SSH_PORT
PUBLIC_HTML_SSH_USER
PUBLIC_HTML_SSH_PRIVATE_KEY
PUBLIC_HTML_REMOTE_PATH
PUBLIC_HTML_SSH_KNOWN_HOSTS
```

### `PUBLIC_HTML_SSH_HOST`

SSH-хост обычного хостинга.

Пример:

```text
server.example.ru
```

### `PUBLIC_HTML_SSH_PORT`

SSH-порт. Обычно:

```text
22
```

Если секрет не задан, workflow использует `22`.

### `PUBLIC_HTML_SSH_USER`

Пользователь SSH на хостинге.

### `PUBLIC_HTML_SSH_PRIVATE_KEY`

Приватный SSH-ключ для деплоя.

Ключ должен быть без пароля или должен поддерживаться текущим способом запуска в GitHub Actions.

Рекомендуется создать отдельный deploy-ключ только для хостинга, а не использовать личный основной SSH-ключ.

### `PUBLIC_HTML_REMOTE_PATH`

Абсолютный путь до папки, которая является корнем сайта `pismolet.ru` на хостинге.

Примеры:

```text
/home/user/pismolet.ru/public_html
/home/user/www/pismolet.ru
/var/www/pismolet.ru
```

Важно: workflow использует `rsync --delete`. Всё, чего нет в `repo/public_html/`, будет удалено из удалённой папки. В корне сайта не нужно хранить ручные файлы, которых нет в Git.

### `PUBLIC_HTML_SSH_KNOWN_HOSTS`

Опционально, но желательно.

Содержимое можно получить локально:

```bash
ssh-keyscan -p 22 HOSTNAME
```

Если секрет не задан, workflow выполнит `ssh-keyscan` сам во время деплоя.

## Настройка на GitHub

Открыть:

```text
Settings -> Secrets and variables -> Actions -> Repository secrets
```

Добавить секреты из списка выше.

## Проверка

После добавления секретов:

1. открыть вкладку `Actions`;
2. выбрать workflow `Deploy public_html to pismolet.ru`;
3. запустить `Run workflow` на ветке `Development`;
4. проверить, что на `pismolet.ru` обновились файлы из `public_html/`.

## Важные правила

1. Для `pismolet.ru` менять только `public_html/`.
2. Для `app.pismolet.ru` использовать отдельный деплой приложения.
3. В `public_html` использовать пути от корня сайта:

```html
<link rel="icon" href="/assets/brand/favicon.svg">
<img src="/assets/brand/logo-horizontal.svg" alt="Письмолёт">
```

Не использовать URL с `/public_html/`, потому что на хостинге эта папка является корнем сайта.
