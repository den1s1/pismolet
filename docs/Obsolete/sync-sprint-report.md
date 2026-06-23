# Sync sprint report

Дата: 2026-06-16

Сделано:

- добавлены целевые проекты Domain, Application, Infrastructure;
- добавлен Docker Compose для PostgreSQL;
- добавлен базовый EF Core DbContext и design-time factory;
- добавлен XLSX-импорт через ClosedXML 0.105.0;
- добавлен ImportBatch;
- добавлена admin-заглушка;
- добавлен базовый проект интеграционных тестов;
- добавлены dev seed-данные.

XLSX: выбран ClosedXML 0.105.0, лицензия MIT.

Ограничения:

- полный перенос классов из Web в целевые проекты оставлен отдельным безопасным шагом;
- runtime repositories пока остаются in-memory;
- CI workflow не добавлен из-за ограничения write tool для `.github/workflows`.
