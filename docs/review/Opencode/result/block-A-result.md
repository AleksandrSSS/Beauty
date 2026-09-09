# Результат блока A — фундамент (Фази 0+1). Статус: ✅ ВИКОНАНО

Дата: 2026-09-04. План: `docs/review/Kiro/masters-feature-plan.md` §6, §7.

## A1. Baseline-замір ДО змін

- `dotnet build server` (net9.0): **0 Warning, 0 Error**.
- API до змін: `services = 6`, `masters = 6`, `admin-login → 200 + token`.
- БД до змін: `Services = 6`, `Masters = 6`, `Appointments = 2`, `Users = 3`, `__EFMigrationsHistory` відсутня (підтверджено — схема від `EnsureCreated()`).
- Бекап: `db-backup-2026-09-04.sql` (38500 байт) у тимчасовій теці поза репозиторієм.

## A2. Фаза 0 — .NET 10

Змінено:
- `server/Beauty.Server.csproj`: `net9.0` → `net10.0`; `EFCore.Design 9.0.0` → `10.0.11`; `Npgsql 9.0.1` → `10.0.3`; `JwtBearer 9.0.0` → `10.0.11`; `BCrypt.Net-Next 4.0.3` → `4.2.1`.
- `README.md`: Backend → ASP.NET Core 10 + «потрібен .NET 10 SDK або новіший».
- `AGENTS.md` §2: стек → net10.0/EF Core 10; позначка Фази 0 «✅ виконано 2026-09-04».
- `global.json` не створював (R11).

Білд після: **0 Warning, 0 Error**, `bin/Debug/net10.0/`.
Відхилення від плану: `docs/review/04-roadmap.md` не існує в репо (є тільки `docs/review/Kiro/` і `docs/design/`) — пункт «оновити P2 sdk:9.0→10.0» пропустив, нема чого правити.

## A3. Smoke-тест Фази 0 (блокуючий — пройдено)

- `admin-login` → **200 + token**: BCrypt 4.2.1 верифікує хеш від 4.0.3 (ризик Р2 не справдився).
- `services = 6`, `masters = 6`, enum рядками.
- `request-code (mock) → verify-code → account/appointments` — OK.
- `slots masterId=1` → **140 слотів**, перший `2026-09-06T06:00:00Z`.
- `create → confirm → cancel` — повний цикл OK (тестовий запис id=3 потім видалено, див. нижче).
- `client: npm run build` — tsc + vite OK (41 модуль, 1.30s).
- Claim-mapping `MobilePhone!` працює на .NET 10 (ризик Р3 не справдився).

## A4. Фаза 1 — baseline

- `dotnet ef migrations add InitialCreate` → `server/Migrations/20260904105842_InitialCreate.cs` + Designer + Snapshot (закомічувати разом з кодом — `Migrations/` у `.gitignore` не додавав).
- `baseline-check.sql` (корінь проєкту, тимчасовий): всі 8 таблиць, `Users.Phone` unique, `MasterServices` композитний PK, індекс `(MasterId, StartTime)`, `RotationType` integer, `RotationAnchor` date, `timestamptz`/`time` — **звірено з фактичною схемою (`pg_dump --schema-only`), 1:1, правити руками не довелось** (ризик Р1 не справдився).
- Позначка: `INSERT INTO "__EFMigrationsHistory" ('20260904105842_InitialCreate', '10.0.11')`.
- `dotnet ef migrations list` → `InitialCreate` застосована.
- `server/Data/DbInitializer.cs` — новий файл, тіло сіду перенесено без зміни семантики, перший рядок `Migrate()`; `AppDbContext.Seed` видалено; `Program.cs` викликає `DbInitializer.Seed`.
- Перезапуск з `Migrate()`: старт без помилок, `services = 6`, `masters = 6` (**дублів нема**), `admin-login` 200.
- Прибирання: smoke-залишки (запис id=3, юзер `+380991112233`, його коди) видалено; фінал БД: `Services = 6`, `Masters = 6`, `Appointments = 2`, `Users = 3` — як до робіт.

## Файли (змінено/створено)

Змінено: `server/Beauty.Server.csproj`, `server/Data/AppDbContext.cs` (прибрано Seed), `server/Program.cs` (1 рядок), `README.md`, `AGENTS.md`.
Створено: `server/Data/DbInitializer.cs`, `server/Migrations/*` (3 файли).
Тимчасові (корінь проєкту, видалити перед комітом): `baseline-check.sql`.

## Наступний крок

Блок B (Фази 2+3): `PhotoUrl` + upload. Залежність — блок A ✅ закрито.
