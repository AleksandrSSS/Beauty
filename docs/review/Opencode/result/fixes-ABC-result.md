# Результат: виправлення за рев'ю блоків A–C. Статус: ✅ 10/10 ЗАКРИТО

Дата: 2026-09-04. Чек-лист: `docs/review/Kiro/fixes-after-blocks-ABC.md`.

## Що зроблено

1. **AGENTS.md §7** — перевірено: уже описує міграції/`DbInitializer`/заборони (застосовано паралельно). Змін не вносив.
2. **AGENTS.md §9** — перевірено: D1–D4 і `ReminderWorker` винесені в «Виправлено блоком C», `Reschedule` посилається на `ValidateAsync`. Змін не вносив.
3. **AGENTS.md §10** — перевірено: Фази 0–4 позначені ✅. Змін не вносив.
4. **UploadPhoto** (`AdminController.cs:157-201`): `ReadExactlyAsync` → `ReadAsync` у циклі з лічильником; `IsImage(byte[], count)` — JPG 3 / PNG 8 / WebP 12 байт. Тест: файл 5 байт → **400** `«Вміст файлу не є зображенням...»` (було 500); валідний PNG 67 байт → 200.
5. **roadmap:45** — `sdk:9.0 → aspnet:9.0` замінено на `sdk:10.0 → aspnet:10.0`.
6. **`baseline-check.sql`** видалено з кореня; `check.sql`/`baseline-check.sql` додано в `.gitignore`.
7. **13-байтовий `1-e40f...60.jpg`** видалено з uploads; `UPDATE "Masters" SET "PhotoUrl" = NULL WHERE "Id" = 1` — перевірено SELECT (порожньо). Папка чиста (`.gitkeep`).
8. **`block-B-result.md` п.2** уточнено: фреймворк дає **413**, гілка `file.Length > maxBytes → 400` недосяжна при рівному конфігу.
9. **masters-feature-plan §3.2** — позначка «зріз станом на 2026-09-03, до блока A», актуальний стан — `AGENTS.md §7`. Плюс рядок-посилання на чек-лист у §20.
10. **`git init`** виконано (без коміту, за забороною §11); `.gitignore` доповнено (`check.sql`, `baseline-check.sql`, `bin/`, `obj/`, `node_modules/`, `client/dist/`, `.vs/`, `TestResults/`).

## Перевірки (§12)

- `dotnet build server` — 0 Warning, 0 Error.
- `admin-login` → 200 (після правок контролерів).
- `slots masterId=1` → 140; `masterId=null` → 284 (обидва режими, форма та сама).
- `migrations list` — `InitialCreate`, `AddMasterPhotoUrl` застосовані.
- Визнання помилки: твердження «04-roadmap.md не існує» в результаті A було хибним — мій glob його пропустив, файл існує. Виправлено п.5 + цей запис.
- Не перевіряв: `npm run build` (клієнт не чіпав — останній білд з блока C зелений).
