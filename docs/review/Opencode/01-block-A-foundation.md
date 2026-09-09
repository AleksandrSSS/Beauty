# Блок A — фундамент: .NET 10 + baseline-міграція (Фази 0, 1)

Джерело: `masters-feature-plan.md` §6, §7. Роадмап: P1-1, C7.

## Мета

Прибрати EOL-рантайм і отримати керовану схему без втрати даних. Після блока схема мігрує через `Migrate()`, а не `EnsureCreated()`.

## Кроки

### A1. Baseline-замір ДО змін (§6.1.1)

1. `dotnet build server` — записати кількість warning'ів.
2. `POST /api/auth/admin-login` поточним паролем → має бути 200 + токен. Записати.
3. `GET /api/catalog/services`, `GET /api/catalog/masters` → 200, enum рядками, ключі camelCase.

### A2. Фаза 0 — .NET 10 (§6.1.2–6.1.7)

1. `server/Beauty.Server.csproj`: `net9.0` → `net10.0`; пакети строго:
   `EFCore.Design 10.0.11`, `Npgsql.EFCore.PG 10.0.3`, `JwtBearer 10.0.11`, `BCrypt.Net-Next 4.2.1`.
2. `global.json` не створювати.
3. `dotnet restore server`, `dotnet build server`.
4. `README.md`: «ASP.NET Core 9» → «ASP.NET Core 10» + «потрібен .NET 10 SDK або новіший».
5. `docs/review/04-roadmap.md` P2: `sdk:9.0 → aspnet:9.0` замінити на `10.0`.

### A3. Smoke-тест Фази 0 (§6.3, блокуючий п.3)

1. Білд без нових warning'ів.
2. `dotnet run --project server` — старт без винятків, `ReminderWorker` у логах.
3. **`POST /api/auth/admin-login` → 200.** Якщо 401 — відкотити тільки `BCrypt.Net-Next` на 4.0.3, решту лишити. Перехешування тільки за окремим рішенням.
4. `request-code → verify-code → account/appointments` з токеном.
5. `POST /api/booking/slots` в обох режимах (`masterId` заданий / `null`) → форма незмінна.
6. Повний цикл `create → confirm`.
7. Клієнт `npm run dev` — головна, `/booking`, `/cabinet`, `/admin` без помилок консолі.

### A4. Фаза 1 — baseline (§7.1–7.5)

1. Бекап обов'язково: `docker exec beauty-db pg_dump -U beauty -d beauty > db-backup-2026-09-04.sql` поза репозиторієм. Перевірити розмір > 0.
2. `dotnet ef migrations add InitialCreate --project server`.
3. `dotnet ef migrations script --project server --output baseline-check.sql` — перевірити що файл не порожній (CLI 10 частину виводу пише в stderr).
4. Звірка по чек-листу §7.3 для 8 таблиць (`Users` unique `Phone`, `Masters.RotationType` integer, `RotationAnchor` date, `MasterServices` композитний PK, `WorkingHours` time, `Appointments` індекс + timestamptz, enum integer). Джерело істини — фактична БД. При розбіжності правити міграцію під БД.
5. DDL історії з початку `baseline-check.sql`, потім `INSERT INTO "__EFMigrationsHistory"`.
6. Контроль: `dotnet ef migrations list --project server` показує `InitialCreate` як застосовану.
7. `server/Data/DbInitializer.cs`: перенести тіло `Seed` без зміни семантики (D8 не чіпати), перший рядок `db.Database.Migrate()`. `AppDbContext.Seed` видалити, у `Program.cs` викликати `DbInitializer.Seed`.
8. Перевірка: `Services` = 6, `Masters` = 6, без дублів; `Appointments`/`Users` count без змін; `GET /api/catalog/masters` → 200 з тими самими даними.

## Перевірка блока

- `dotnet build server`, `dotnet ef migrations list --project server`.
- Smoke A3 повторно.
- `SELECT count(*)` по `Services`, `Masters`, `Appointments`, `Users` до/після.

## Відкат

- Код: `git revert` (Фаза 0 і Фаза 1 — окремі коміти, але не комічу без запиту — тримаю зміни в робочій копії роздільно).
- БД: `DROP TABLE "__EFMigrationsHistory"`; у найгіршому разі — дамп з A4.1.

## Ризики

- Р1: розбіжність `EnsureCreated EF9` vs `InitialCreate EF10` — середня ймовірність, закладаю час на ручну правку.
- Р2: BCrypt 4.2.1 vs хеш 4.0.3 — низька, але блокуюча; шлях відкату вище.
- Р3: claim-mapping `MobilePhone!` — smoke п.4 і п.6 це покажуть.

Оцінка: 3,5–7 год.
