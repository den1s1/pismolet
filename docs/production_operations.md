# Production-эксплуатация Письмолёта

Дата актуализации: 2026-07-10  
Контур: production  
Ответственное направление: техническое

Документ фиксирует общие серверные договорённости, которые применяются ко всем техническим веткам проекта. Специализированные инструкции могут дополнять его, но не должны противоречить ему.

## 1. Текущая раскладка production

| Назначение | Путь или имя |
|---|---|
| Git-репозиторий с исходниками | `/opt/pismolet` |
| Production-ветка исходников | `Development` |
| Каталог активного опубликованного приложения | `/var/www/pismolet` |
| systemd-сервис | `pismolet.service` |
| Запускаемая сборка | `/var/www/pismolet/Pismolet.Web.dll` |
| Production EnvironmentFile | `/etc/pismolet/pismolet.env` |
| Runtime-данные приложения | `/var/lib/pismolet` |
| Каталог production-backup | `/var/backups/pismolet` |
| Установленные эксплуатационные команды | `/usr/local/bin` |
| Канонические версии команд в Git | `scripts/production` |

Перед изменением путей сначала обновить этот документ и связанные инструкции.

## 2. Требования к Git-каталогу

Перед сборкой или деплоем репозиторий должен:

- находиться на ветке `Development`, а не в состоянии detached HEAD;
- не содержать изменений отслеживаемых или неотслеживаемых файлов;
- быть синхронизирован с `origin/Development` только fast-forward-операцией.

Проверка:

```bash
git -C /opt/pismolet status --short --branch
```

Ожидаемый вид:

```text
## Development...origin/Development
```

Обновление:

```bash
git -C /opt/pismolet pull --ff-only origin Development
```

Не использовать обычный `git pull`, который может создать merge-коммит на production-сервере.

Каталоги `bin/` и `obj/` являются временными результатами сборки и не должны попадать в Git. Перед удалением любых неотслеживаемых файлов обязательно просмотреть `git status`; не применять бездумно `git clean -fd` на production.

## 3. Production EnvironmentFile

Фактический файл конфигурации и секретов:

```text
/etc/pismolet/pismolet.env
```

Он подключается к `pismolet.service` директивой systemd `EnvironmentFile=`. Проверить это можно без вывода содержимого файла:

```bash
sudo systemctl cat pismolet
```

Файл не хранится в Git и не должен попадать в архивы, логи, скриншоты или сообщения чатов.

Рекомендуемые права:

```bash
sudo chown root:pismolet /etc/pismolet/pismolet.env
sudo chmod 640 /etc/pismolet/pismolet.env
```

Перед каждым изменением обязательна резервная копия:

```bash
sudo cp -a \
  /etc/pismolet/pismolet.env \
  "/etc/pismolet/pismolet.env.backup-$(date -u +%Y%m%d-%H%M%S)"
```

Редактировать через `sudoedit` или другой редактор, не выводящий весь файл в терминал:

```bash
sudoedit /etc/pismolet/pismolet.env
```

После изменения проверить права, перезапустить сервис и выполнить health-check.

## 4. Важное различие systemd EnvironmentFile и shell

`/etc/pismolet/pismolet.env` предназначен для systemd и не должен подключаться командами:

```bash
. /etc/pismolet/pismolet.env
source /etc/pismolet/pismolet.env
```

Причина: синтаксис EnvironmentFile и shell различается. Например, строка подключения Npgsql содержит `;`. При выполнении файла как shell-скрипта точка с запятой воспринимается как разделитель команд, и переменная может быть обрезана до первой части, например до `Host=localhost`.

Факт такого сбоя подтверждён 2026-07-10 при ручном запуске EF migration: приложение и сам env-файл были исправны, но shell-загрузка передала неполную строку подключения и PostgreSQL отклонил соединение из-за отсутствующего пароля.

Запрещено исправлять это добавлением кавычек или shell-конструкций в production-файл без отдельной проверки совместимости с systemd. Безопаснее извлекать только нужную переменную как данные и передавать её одной команде.

## 5. Безопасная проверка наличия переменной

Не выводить значение секрета или строки подключения. Для проверки наличия и длины конкретной переменной:

```bash
sudo awk '
  index($0, "ConnectionStrings__PismoletDb=") == 1 {
    value = substr($0, index($0, "=") + 1)
    printf "ConnectionStrings__PismoletDb=set, length=%d\n", length(value)
    found = 1
  }
  END {
    if (!found) print "ConnectionStrings__PismoletDb=unset"
  }
' /etc/pismolet/pismolet.env
```

Для секретов достаточно проверять `set/unset`; длину публиковать только когда это действительно нужно для диагностики.

Не выполнять:

```bash
cat /etc/pismolet/pismolet.env
grep TOKEN /etc/pismolet/pismolet.env
printenv
```

Такие команды могут раскрыть секреты в терминале, истории команд или чате.

## 6. Безопасная передача строки подключения в EF CLI

Для design-time-команд EF извлечь только нужную строку как данные, не выполнять весь env-файл:

```bash
cd /opt/pismolet

connection_string="$(
  sudo awk '
    index($0, "ConnectionStrings__PismoletDb=") == 1 {
      print substr($0, index($0, "=") + 1)
    }
  ' /etc/pismolet/pismolet.env \
  | tail -n 1
)"

test -n "$connection_string" || {
  echo "ConnectionStrings__PismoletDb не найдена" >&2
  exit 1
}

env "ConnectionStrings__PismoletDb=$connection_string" \
  dotnet ef database update \
    --context <DbContext> \
    --project <путь-к-проекту-с-миграциями> \
    --startup-project src/Pismolet.Web/Pismolet.Web.csproj

unset connection_string
```

Подставлять конкретный DbContext и проект из специализированной инструкции. Не добавлять вывод `connection_string`, `set -x` или трассировку окружения.

## 7. Штатные эксплуатационные команды

Канонические версии:

```text
scripts/production/build-pismolet
scripts/production/run-tests-pismolet
scripts/production/deploy-pismolet
```

Установленные команды:

```text
/usr/local/bin/build-pismolet
/usr/local/bin/run-tests-pismolet
/usr/local/bin/deploy-pismolet
```

Установка или обновление:

```bash
cd /opt/pismolet

sudo install -o root -g root -m 755 \
  scripts/production/build-pismolet \
  /usr/local/bin/build-pismolet

sudo install -o root -g root -m 755 \
  scripts/production/run-tests-pismolet \
  /usr/local/bin/run-tests-pismolet

sudo install -o root -g root -m 755 \
  scripts/production/deploy-pismolet \
  /usr/local/bin/deploy-pismolet
```

Назначение:

- `build-pismolet` — проверяет ветку и чистоту дерева, делает `git pull --ff-only`, собирает решение;
- `run-tests-pismolet` — запускает тесты и принимает дополнительные параметры `dotnet test`;
- `deploy-pismolet` — выполняет сборку, тесты, staging publish, переключение релиза, запуск, строгий health-check и автоматический откат при ошибке.

EF-миграции намеренно не входят в `deploy-pismolet`: изменение схемы БД выполняется отдельно, после backup и по специализированному rollout-плану.

## 8. Обязательные backup перед production-изменениями

Перед изменением БД создать dump. Перед заменой текущих бинарников или изменением серверной конфигурации создать соответствующую резервную копию.

Пример backup PostgreSQL:

```bash
backup="/var/backups/pismolet/pismolet-before-change-$(date -u +%Y%m%d-%H%M%S).dump"

sudo install -d -m 700 -o postgres -g postgres /var/backups/pismolet
sudo -u postgres pg_dump -Fc -d pismolet -f "$backup"
sudo chmod 600 "$backup"
sudo ls -lh "$backup"
```

Пример архива активного приложения:

```bash
backup="/var/backups/pismolet/pismolet-app-before-change-$(date -u +%Y%m%d-%H%M%S).tar.gz"

sudo tar -C /var/www -czf "$backup" pismolet
sudo chmod 600 "$backup"
sudo ls -lh "$backup"
```

Путь, размер и права backup фиксировать в rollout-отчёте. Содержимое backup не добавлять в Git.

## 9. Порядок выкладки изменений со схемой БД

Безопасный порядок:

1. Подтвердить зелёные сборку и тесты нужного кодового HEAD.
2. Проверить ветку `Development` и чистоту рабочего дерева.
3. Создать backup БД; при необходимости — backup env-файла и активного приложения.
4. Обновить исходники fast-forward-операцией.
5. Применить additive-миграцию отдельной командой.
6. Проверить запись миграции и ожидаемые объекты БД.
7. Выполнить `deploy-pismolet`.
8. Проверить systemd, HTTPS health-check, функциональный smoke и логи.
9. Подтвердить, что обычная отправка писем не изменилась.
10. Только после smoke отметить спринт как `внедрён`.

При несовместимой миграции порядок должен быть отдельно спроектирован. Не применять общий порядок механически.

## 10. Диагностика сервиса без раскрытия секретов

Основные команды:

```bash
sudo systemctl show pismolet \
  -p WorkingDirectory \
  -p ExecStart \
  -p EnvironmentFiles \
  --no-pager

sudo systemctl status pismolet --no-pager

curl --fail --silent --show-error \
  https://app.pismolet.ru/health

sudo journalctl -u pismolet -n 200 --no-pager
```

Перед отправкой логов в чат проверить, что они не содержат секреты. Для первичной проверки использовать только число совпадений:

```bash
sudo journalctl -u pismolet -n 500 --no-pager \
  | grep -Eci 'refresh_token|access_token|Authorization|Bearer'
```

Ожидаемый результат — `0`. При ненулевом результате не публиковать совпавшие строки; сначала локально определить и удалить или отредактировать секретные значения.

## 11. Откат и хранение предыдущих релизов

`deploy-pismolet` сохраняет предыдущий активный релиз в каталоге вида:

```text
/var/www/pismolet.rollback-YYYYMMDD-HHMMSS
```

При ошибке запуска или health-check скрипт пытается автоматически вернуть этот релиз.

После успешного smoke предыдущий релиз не удалять немедленно. Периодически удалять устаревшие rollback-каталоги вручную после проверки даты и наличия независимого backup. Не использовать широкие маски удаления без предварительного `ls -ld`.

Новая additive-таблица обычно может остаться при откате старых бинарников, но это решение должно быть явно зафиксировано в rollout-документе конкретной миграции.

## 12. Что необходимо фиксировать после production-работ

В основном документе текущего технического направления записать:

- дату и фактический кодовый HEAD;
- путь и размер созданных backup;
- применённые миграции;
- состояние сервиса и результат HTTPS health-check;
- результат функционального smoke;
- результат проверки логов без публикации секретов;
- результат контрольной отправки письма, если изменение могло затронуть отправку;
- решение по откату или сохранённому rollback-каталогу;
- итоговый статус спринта.
