# Техническая HTTP-проверка сайта

Дата: 2026-07-08  
Раздел: SEO и маркетинг  
Статус: выполнено

## Проверенные URL

Проверка выполнена из Windows PowerShell.

Все ключевые URL вернули `200 OK`:

- `https://pismolet.ru/`;
- `https://pismolet.ru/sitemap.xml`;
- `https://pismolet.ru/robots.txt`;
- `https://pismolet.ru/articles/rassylka-v-yandex-pochte/`;
- `https://pismolet.ru/articles/kak-sdelat-rassylku-klientam/`;
- `https://pismolet.ru/rassylka-pisem/`;
- `https://pismolet.ru/rassylka-klientam/`.

## robots.txt

`https://pismolet.ru/robots.txt` доступен и содержит:

```txt
User-agent: *
Allow: /

Sitemap: https://pismolet.ru/sitemap.xml
```

Вывод:

- публичные страницы не закрыты от индексации;
- sitemap явно указан для поисковых роботов.

## sitemap.xml

`https://pismolet.ru/sitemap.xml` доступен и содержит 27 HTTPS-URL.

Ключевые страницы присутствуют:

- главная `/`;
- `/rassylka-pisem/`;
- `/rassylka-klientam/`;
- `/articles/`;
- `/articles/rassylka-v-yandex-pochte/`;
- `/articles/kak-sdelat-rassylku-klientam/`;
- `/bcc-alternative/`;
- `/excel-email-list/`;
- `/no-domain-setup/`;
- `/contacts/`;
- юридические страницы.

Вывод:

- sitemap соответствует текущему публичному сайту;
- количество URL совпадает с числом страниц, выявленных Google Search Console: 27;
- технических блокировок по HTTP, robots.txt и sitemap на этом этапе не обнаружено.

## Следующие действия

1. Не повторять запросы индексирования в Google Search Console сразу после отправки.
2. Дождаться обработки запросов индексирования для четырёх ключевых страниц.
3. Через 2–3 дня проверить статусы в Google Search Console и Яндекс.Вебмастере.
4. Через 7–14 дней проверить выдачу по брендовым и приоритетным запросам.
5. Если рекомендация Яндекс.Вебмастера `Файл favicon не найден` сохранится после переобхода, добавить `public_html/favicon.ico`.