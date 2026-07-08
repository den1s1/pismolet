# Проверка URL в Google Search Console

Дата: 2026-07-08  
Раздел: SEO и маркетинг  
Статус: в работе

## 1. Контекст

Проверка проводится после подтверждения доменного ресурса `pismolet.ru` в Google Search Console и успешной обработки sitemap `https://pismolet.ru/sitemap.xml`.

Sitemap обработан успешно, Google выявил 27 страниц.

## 2. Проверенные URL

### `https://pismolet.ru/`

Статус: в индексе Google.

Наблюдения:

- URL есть в индексе Google;
- страница проиндексирована;
- блок `Индексирование страниц` показывает, что страница проиндексирована;
- HTTPS подтверждён.

Решение:

- дополнительных действий по главной странице сейчас не требуется;
- кнопку `Запросить индексирование` можно не нажимать, если нет свежих изменений, которые нужно срочно переобойти.

### `https://pismolet.ru/articles/rassylka-v-yandex-pochte/`

Статус: не в индексе Google.

Наблюдения:

- URL обнаружен, но не проиндексирован;
- источник обнаружения: `https://pismolet.ru/sitemap.xml`;
- также указана ссылающаяся страница `http://pismolet.ru/`;
- последнее сканирование отсутствует;
- робот, выполнивший сканирование, отсутствует;
- получение страницы отсутствует;
- данные по canonical отсутствуют, потому что страница ещё не сканировалась.

Интерпретация:

- это не выглядит как ошибка страницы;
- Google уже знает URL из sitemap, но ещё не успел его просканировать;
- упоминание `http://pismolet.ru/` как ссылающейся страницы не является отдельной проблемой, если HTTP-версия корректно перенаправляет на HTTPS.

Следующее действие:

- нажать `Запросить индексирование` для этой страницы.

## 3. Ещё не проверено

Проверить через URL Inspection:

- `https://pismolet.ru/articles/kak-sdelat-rassylku-klientam/`;
- `https://pismolet.ru/rassylka-pisem/`;
- `https://pismolet.ru/rassylka-klientam/`.

Приоритет:

1. `https://pismolet.ru/articles/kak-sdelat-rassylku-klientam/`;
2. `https://pismolet.ru/rassylka-pisem/`;
3. `https://pismolet.ru/rassylka-klientam/`.

## 4. Текущий ближайший шаг

1. В Google Search Console нажать `Запросить индексирование` для `https://pismolet.ru/articles/rassylka-v-yandex-pochte/`.
2. Проверить `https://pismolet.ru/articles/kak-sdelat-rassylku-klientam/`.
3. Если вторая статья тоже не в индексе, нажать `Запросить индексирование`.
4. После двух статей проверить `/rassylka-pisem/` и `/rassylka-klientam/`.