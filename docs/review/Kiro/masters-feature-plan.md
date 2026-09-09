# План: фото майстрів, сторінка графіка з бронюванням, керування персоналом

**Статус:** готовий до виконання. **Дата:** 2026-09-03. **Автор:** Kiro (узгоджено з користувачем покроково).
**Обсяг:** 13 фаз, ~21–25 год. **Зачіпає:** ~30 файлів (бекенд + клієнт + інфра).

---

## 1. Мета

Два початкові запити користувача:

1. Картка майстра на головній сторінці має бути **з фото**.
2. Клік по майстру веде на **його графік роботи з можливістю бронювання часу на послугу**.
3. Вибір часу — через **календар з датами**: клік на дату показує вільні слоти. На `/booking` — слоти всіх майстрів; при вході через картку майстра — календар його робочих днів і слоти лише цього майстра.
4. Глибина бронювання — **два тижні**.

У процесі узгодження обсяг розширено технічними передумовами (EF-міграції, .NET 10, рефакторинг слотів) і доменною логікою (звільнення/найм майстрів), без яких перші два пункти або не реалізуються, або дають некоректну поведінку.

---

## 2. Зафіксовані рішення

| # | Питання | Рішення | Обґрунтування |
|---|---|---|---|
| R1 | Спосіб задання фото | **Upload файлу через адмінку** у `server/wwwroot/uploads/masters`, валідація, ліміт 3 МБ | Обрано користувачем замість поля-URL |
| R2 | Схема БД | **EF Core Migrations через baseline**, без втрати даних; `Migrate()` замість `EnsureCreated()` | Обрано користувачем; збігається з `04-roadmap.md` **P1-1** |
| R3 | Форма сторінки майстра | **Окремий роут `/masters/:id`** (варіант B: клік на майстра веде на його сторінку, а не одразу в `/booking`) | Обрано користувачем |
| R4 | Порядок вибору | **Спочатку послуга → потім календар → дата → слоти** (варіант i) | Слоти залежать від `Service.DurationMin`; календар без відомої тривалості обіцяв би час, якого немає |
| R13 | Вибір часу — UI | **Календар з датами як окремий крок**, клік на дату → слоти лише цієї дати. Замість поточного списку «усі 14 днів одразу». Спільний компонент `BookingCalendar` для `/booking` і `/masters/:id` | Обрано користувачем |
| R14 | Глибина вікна в календарі | **`сьогодні … сьогодні+14`** (15 дат включно) — як `ScheduleService.WindowDays`. Дати поза діапазоном — `disabled` | Збігається з наявним бекендом, нових ендпоінтів не потрібно |
| R15 | Джерело доступних дат | `/booking`: з відповіді `POST /api/booking/slots` (групування по салонній даті). `/masters/:id`: з `GET /api/catalog/masters/{id}/schedule` (`isWorking`), після вибору послуги — перетин з наявністю слотів | **Нових ендпоінтів не додаємо** |
| R5 | Логіка слотів | Винести з `BookingController` у **`server/Services/ScheduleService.cs`**, контролер делегує | Обрано користувачем; дублювання правил графіка неприйнятне |
| R6 | Бронювання зі сторінки майстра | **`navigate('/booking', { state })`**, `Booking.tsx` читає `location.state` | Обрано користувачем (варіант C1) — один флоу підтвердження кодом |
| R7 | Сховище файлів | **D1** — диск сервера; шлях у конфігурації; бекап папки окремо від `pg_dump` | Сервер не контейнеризований, CI немає → D2 передчасний |
| R8 | Керування персоналом | **Входить у план** (Фази 11–12) | Обрано користувачем |
| R9 | Рантайм | **Перехід на .NET 10 (LTS)** першою фазою | .NET 9 виходить з підтримки 10.11.2026 |
| R10 | `BCrypt.Net-Next` 4.0.3 → 4.2.1 | **Входить у Фазу 0** | Обрано користувачем |
| R11 | `global.json` | **Не додаємо** | Гнучкість; найвищий встановлений SDK і так 10.x. Вимогу фіксуємо текстом у `README.md` |
| R12 | Назва спільного компонента правил | `ScheduleService` **виконує роль** `BookingValidator` з роадмапу **P1-2** | Щоб не було двох паралельних реалізацій правил |

---

## 3. Перевірений стан (факти, не припущення)

### 3.1. Оточення

- `dotnet --list-sdks`: `9.0.306`, `10.0.204`, **`10.0.400`**.
- `dotnet --list-runtimes`: `Microsoft.NETCore.App` і `Microsoft.AspNetCore.App` — 8.0.21, 8.0.30, 9.0.10, 9.0.19, **10.0.8, 10.0.11**.
- `dotnet ef --version` → **10.0.8** (мажорно новіший за EF-рантайм 9 у проєкті — знімається Фазою 0).
- `dotnet user-secrets list --project server`: ключі `Jwt:Key` і `Admin:Password` **задані** → EF-tools зможуть побудувати host без `IDesignTimeDbContextFactory`.
- Немає: `Dockerfile`, `.dockerignore`, `*.sln`, папки `server/Migrations`, тестового проєкту.

Блок `.env` — конфіг оточення, секрети поза проєктом.
- Кореневого `.env` **немає**, хоч `docker-compose.yml` має `env_file: [.env]` → `docker compose up` впаде без нього (передумова, не частина плану).
- `client/.env`: `VITE_API_URL=http://localhost:5000` — тобто клієнт звертається **напряму на порт 5000**, а не через Vite-проксі. Наслідок для Фази 3: фото за відносним шляхом треба склеювати з `VITE_API_URL`, а не покладатись на проксі.

### 3.2. Стан БД (`docker exec beauty-db psql -U beauty -d beauty`)

> **Увага: зріз станом на 2026-09-03, до блока A.** Після Фази 1 є `__EFMigrationsHistory` (`InitialCreate` baseline + `AddMasterPhotoUrl`), `Masters` має `PhotoUrl text NULL`. Актуальний стан — див. `AGENTS.md §7`.

- Контейнер `beauty-db` — `Up (healthy)`, том `beauty_beauty_pgdata` існує, дані є.
- 8 таблиць: `Appointments`, `MasterServices`, `Masters`, `NotificationLogs`, `Services`, `Users`, `VerificationCodes`, `WorkingHours`.
- **`__EFMigrationsHistory` відсутня** → схему створив `EnsureCreated()`, baseline обов'язковий.
- `Masters`: `Id integer NOT NULL`, `Name text NOT NULL`, `Specialty text NULL`, `Bio text NULL`, `IsActive boolean NOT NULL`, `RotationType integer NOT NULL`, `RotationAnchor date NOT NULL`. Колонки `PhotoUrl` **немає**.

### 3.3. Версії пакетів

| Пакет | У `server/Beauty.Server.csproj` | Найновіша (NuGet, перевірено) |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Design` | 9.0.0 | **10.0.11** |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 9.0.1 | **10.0.3** |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 9.0.0 | **10.0.11** |
| `BCrypt.Net-Next` | 4.0.3 | **4.2.1** |

`TargetFramework` = `net9.0`, `Nullable` = enable, `ImplicitUsings` = enable, `UserSecretsId` = `beauty-salon-dev-secrets`.

### 3.4. Життєвий цикл .NET

Джерело: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), оновлено 11.08.2026.

| Версія | Тип | Фаза | Кінець підтримки |
|---|---|---|---|
| .NET 10 | **LTS** | Active | 14.11.2028 |
| .NET 9 | STS | Maintenance | **10.11.2026** |

### 3.5. Клієнт

React `^18.3.1`, react-router-dom `^6.26.0`, Vite `^5.4.0`, TypeScript `^5.5.4`.
`npm run build` = `tsc -b && vite build`.
`vite.config.ts`: порт 5173, проксі лише `'/api' → http://localhost:5000`.

---

## 4. Прив'язка до наявного рев'ю та роадмапу

Прочитані документи: `docs/review/00-README.md`, `01-backend.md`, `02-frontend.md`, `03-infra-qa.md`, `04-roadmap.md`, `docs/review/Kiro/quick-wins-check.md`, `docs/design/PLAN.md`.

### 4.1. Задачі роадмапу, які цей план виконує (не дублювати!)

| Роадмап | Опис | Де в цьому плані |
|---|---|---|
| **P1-1** (1д) | EF міграції + `DbInitializer`, прибрати `EnsureCreated` | Фаза 1 |
| **P1-2** (2д) | Єдиний компонент правил для `Slots`/`Create`/`Reschedule` (перетини, `IsActive`, `MasterService`, вікно 14д) | Фаза 4 (`ScheduleService`) |
| **C7** | «Міграцій нема взагалі» | Фаза 1 |
| **M4** | `Slots` і `Create` розходяться → «UI показує вільно, API дає 409» | Фаза 4 |
| **M6** | `DateOnly.FromDateTime(DateTime.UtcNow)` замість `SalonClock.TodayInSalon` | Фаза 4 |
| `RotationHelper` negative-modulo | Weekly до якоря дає невірні дні | Фаза 4 |
| **P0-5 / C3** частково | Валідація при зміні запису адміном | Фаза 11 (через `ScheduleService.ValidateAsync`) |
| `02-frontend.md` §2.5 | Форма майстра не редагує `bio`/`isActive` | Фази 10, 12 |
| `02-frontend.md` §2.2 | Контракт `MasterDto.services` розсинхронений, замаскований `?.` | Фаза 7 |

### 4.2. Що план **не** робить (лишається в роадмапі)

`P0-1` (`CodeHash`/`AttemptCount`), `P0-2` (`AddRateLimiter`), `P0-4` (`RequestId`), `P0-6` (`RefreshTokens`), `C4` повністю (матриця статусів + `AuditLog`), `M3`, `M7`/`P1-4` (`RetryCount`), `M8` (FluentValidation усіх DTO), `M9`/`P1-6` (пагінація `admin/appointments`), `M10`, `M11` (фіксований якір ротації), `M12` (`UseExceptionHandler`, security headers), Dockerfile, CI, тестовий проєкт, підключення `design/tokens.css`, Outbox для нотифікацій.

### 4.3. Наслідки для порядку робіт

- Baseline (Фаза 1) треба зняти **до** P0-задач, що змінюють схему (`CodeHash`, `RequestId`, `AttemptCount`, `AuditLog`, `RefreshTokens`, `RetryCount`) — інакше кожна з них ускладнить baseline.
- Після Фази 0 пункт **P2** роадмапу («Dockerfile `sdk:9.0 → aspnet:9.0`») стає застарілим — оновити текст на `10.0`. Фактичного коду це не ламає: Dockerfile не існує.
- Патчі `P0-2` (`AddRateLimiter`) після Фази 0 треба перевіряти на рантаймі 10.

### 4.4. Конвенції проєкту, яких план тримається

- **Час:** у БД UTC, на UI — «настінний» час салону; будь-яка конвертація **тільки через `SalonClock`** (`ToUtc(day,time)`, `ToSalon(utc)`, `TodayInSalon`, `Format(utc)`). TZ `Europe/Kyiv` з `Salon:TimeZone`.
- **М'яке видалення** зі збереженням історії (`Service.IsActive`, `Master.IsActive`) — «звільнення» йде цим шляхом, не `DELETE`.
- **Транзакція `IsolationLevel.Serializable`** у `BookingController.Create` — не ламати.
- Індекси задаються в `AppDbContext.OnModelCreating`.
- Помилки API — `{ "error": "текст українською" }`; успіх — `{ "ok": true }` або корисне навантаження.
- JSON: `camelCase` + `JsonStringEnumConverter` (enum як рядки) — контракт з `client/src/types.ts`.
- Нові ендпоінти мають **явний** `[Authorize]` або `[AllowAnonymous]` (§1.4 рев'ю: дефолт авторизації планують змінити).
- Мова UI — українська; коментарі в коді — українською, як у наявних файлах.
- Стилі — на CSS-змінних із `client/src/styles.css` (`--accent`, `--glass-10/15/30`, `--radius`, `--shadow-soft`, `--text-dim`). `design/tokens.css` не підключений — не покладатись на нього.

---

## 5. Відомі дефекти, що впливають на цей функціонал

| ID | Файл:рядок | Суть | Входить у план? |
|---|---|---|---|
| D1 | `CatalogController.cs:20` | `DateOnly.FromDateTime(DateTime.UtcNow)` замість `clock.TodayInSalon` → на межі доби (23:30 UTC = 01:30 Kyiv) графік зсувається на день | **ТАК**, Фаза 4/5 |
| D2 | `RotationHelper.cs:18` | Weekly: `(negative / 7) % 2` → невірні робочі дні для дат **до** якоря | **ТАК**, Фаза 4 |
| D3 | `BookingController.cs:33-37` vs `88-91` | `Slots` враховує лише `Confirmed`; `Create` ще й `Pending` за останні 15 хв → UI показує вільно, API дає 409 | **ТАК**, Фаза 4 |
| D4 | `BookingController.Slots` | Не перевіряє `Service.IsActive` | **ТАК**, Фаза 4 |
| D5 | `Admin.tsx` | Форма майстра **не має полів** `bio` та `isActive` → неможливо повернути звільненого майстра з UI | **ТАК**, Фази 10, 12 |
| D6 | `Admin.tsx:1` | BOM/кодування: файл починається з `\uFEFF`; за `PLAN.md` і `03-infra-qa.md` §3.4 — мохібейк кирилиці | **ТАК** (перевірити перед редагуванням) |
| D7 | `Home.tsx:11-13` | `fetchServices()/fetchMasters()` без `catch`, `loading`, `AbortController` | **ТАК** (мінімально: `loading` + `catch`), Фаза 8 |
| D8 | `AppDbContext.cs:53-54,97` | Seed: `DateTime.UtcNow` замість `SalonClock`; `(DayOfWeek)d` де `0=Sunday` | Частково: Фаза 1 (переніс у `DbInitializer` без зміни семантики) |
| D9 | `AdminController.cs:156`, `BookingController.cs:75` | `SpecifyKind(dto.StartTime, Utc)` — якщо клієнт шле час без `Z`, запис зсувається | **Частково**: для **нових** ендпоінтів фіксуємо контракт ISO-8601 з `Z`; наявний `reschedule` не чіпаємо (це C3) |
| D10 | `AuthController.cs:89` | `adminPhone` оголошена й не використовується (dead code) | НІ (косметика) |
| D11 | `Master.RotationAnchor` у seed | Якір залежить від дня першого запуску (M11) → різні інсталяції дають різні графіки | НІ (окреме рішення, змінює дані) |
| D12 | `02-frontend.md` §2.6 | `rgba(255,255,255,.6)` на склі/фото — ймовірний провал WCAG AA | **ТАК** для нових елементів: текст на фото — `--text`, не `--text-dim` |

---

## 6. Фаза 0 — Перехід на .NET 10 (LTS)

**Мета:** прибрати EOL-рантайм і узгодити версію EF з CLI **до** генерації baseline-міграції.
**Чому першою:** (а) знімає розбіжність `dotnet ef` 10.0.8 з EF-рантаймом 9; (б) baseline, згенерований EF 10, не доведеться регенерувати після апгрейду.
**Окрема зміна**, без змішування з функціоналом.

### 6.1. Кроки

1. **Baseline-перевірка ДО змін** (щоб було з чим порівнювати):
   - `dotnet build server` — зафіксувати кількість warning'ів;
   - `POST /api/auth/admin-login` з поточним паролем → **200 + токен**. Записати результат.
2. **`server/Beauty.Server.csproj`:**
   ```xml
   <TargetFramework>net10.0</TargetFramework>
   ```
   ```xml
   <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.11">
     <PrivateAssets>all</PrivateAssets>
   </PackageReference>
   <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
   <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.11" />
   <PackageReference Include="BCrypt.Net-Next" Version="4.2.1" />
   ```
   Версії — точні, без діапазонів (у .NET 10 SDK `PackageReference` без версії — помилка).
3. `global.json` **не створювати** (R11).
4. `dotnet restore server`, `dotnet build server`.
5. Smoke-тест (п. 6.3).
6. `README.md`: «ASP.NET Core 9» → «ASP.NET Core 10»; у «Швидкий старт» додати «потрібен .NET 10 SDK або новіший».
7. `docs/review/04-roadmap.md`, пункт P2: `sdk:9.0 → aspnet:9.0` замінити на `sdk:10.0 → aspnet:10.0`.

### 6.2. Breaking changes: що перевірити

Досліджено офіційні індекси: [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0), [ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview?view=aspnetcore-10.0), [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes), [міграція 9→10](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0), [EFCore.PG 10.0](https://www.npgsql.org/efcore/release-notes/10.0.html). Контент джерел перефразовано відповідно до ліцензійних обмежень.

**Стосується нас:**

| Зміна | Наш код | Дія |
|---|---|---|
| [`BackgroundService` виконує весь `ExecuteAsync` на фоновому потоці](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task) | `ReminderWorker.ExecuteAsync` | Перевірити: перед першим `await` у нас лише вхід у `while` — ініціалізації, на яку хтось розраховує на старті, немає. Ризик низький, але переконатись у логах, що воркер піднявся |
| [EF: parameterized collections → окремі скалярні параметри](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes) | `BookingController.Slots`: `masterIds.Contains(a.MasterId)` | Функціонально сумісно; можлива зміна плану запиту. Дія: після Фази 4 перевірити, що слоти генеруються так само |
| EF: спрощені імена SQL-параметрів | Логери/тести на текст SQL | У нас таких немає → НІ |
| [EF tools вимагають `--framework` для multi-targeting](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes) | `<TargetFramework>` один | НІ |
| [`dotnet` CLI пише частину виводу у stderr](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/dotnet-cli-stderr-output) | `dotnet ef migrations script > file.sql` у Фазі 1 | **ТАК**: перевірити, що файл не порожній і не містить сміття |
| EF 10 вимагає .NET 10 SDK і рантайм | — | Виконано п. 2 |

**Не стосується (перевірено відсутність у списках):** `WebApplication.CreateBuilder`; `AddControllers().AddJsonOptions` з `CamelCase` + `JsonStringEnumConverter`; `AddCors` з `WithOrigins/AllowAnyHeader/AllowAnyMethod`; `app.UseStaticFiles()`; `UseAuthentication/UseAuthorization/MapControllers`; `IFormFile`, `RequestSizeLimit`, `RequestFormLimits`, `IWebHostEnvironment.ContentRootPath`; cookie-редиректи (у нас лише JWT); Npgsql array/`Contains`, computed columns, мережеві типи (не використовуються).

**Не підтверджено офіційними джерелами (перевіряти емпірично):**

1. Фактична робота `ClaimTypes.MobilePhone`, `ClaimTypes.NameIdentifier`, `ClaimTypes.Role` та `[Authorize(Roles="Admin")]` після апгрейду. Задокументованих змін claim-mapping у .NET 10 немає (ключова зміна була в .NET 8 — перехід на `JsonWebTokenHandler`), але поведінку саме нашої конфігурації офіційні джерела не описують. **Критично:** `BookingController` двічі робить `User.FindFirst(ClaimTypes.MobilePhone)!.Value` з `!` → при зміні маппінгу буде `NullReferenceException`, а не зрозуміла помилка.
2. Deprecation `JwtSecurityTokenHandler` / `System.IdentityModel.Tokens.Jwt` у .NET 10 — сторінки, що це стверджує, не знайдено.
3. Зміна дефолту `JwtBearerOptions.MapInboundClaims` (документація вказує `true`, версійних приміток немає).
4. Ідентичність схеми `EnsureCreated()` на EF 9 і `InitialCreate` на EF 10 для `DateOnly`/`TimeOnly`/enum-як-integer/`decimal`/`DateTime(Utc)`→`timestamptz`. Офіційного джерела не існує в принципі → **тільки емпірична звірка у Фазі 1**.
5. Індекс breaking changes .NET 10 позначений як work-in-progress → перелік не остаточний.

### 6.3. Smoke-тест (критерій приймання Фази 0)

Усі пункти обов'язкові; п. 3 — **блокуючий**.

1. `dotnet build server` — без нових warning'ів проти зафіксованого в п. 6.1.1.
2. `dotnet run --project server` — старт без винятків; у логах видно, що `ReminderWorker` працює.
3. **`POST /api/auth/admin-login`** з тим самим паролем → **200 + токен**. Це перевірка, що `BCrypt.Net-Next` 4.2.1 верифікує хеш, створений 4.0.3.
   - Cost-фактор зашитий у сам рядок хешу (`$2a$<cost>$...`), тому зміна *дефолтного* work factor впливає лише на нові хеші.
   - `DbInitializer`/`Seed` створює адміна лише за умови «адміна ще немає» → існуючий `PasswordHash` не перезаписується.
   - **Якщо 401:** відкотити `BCrypt.Net-Next` на 4.0.3 (решта оновлень незалежна) і винести це в окрему задачу. Перехешування адміна робити **тільки** за окремим рішенням — це знищує обліковку.
4. `GET /api/catalog/services` і `GET /api/catalog/masters` → 200; enum-и як рядки, ключі `camelCase`.
5. `POST /api/auth/request-code` → 200 + `devCode` у Development; `POST /api/auth/verify-code` → 200 + токен; токен приймається на `GET /api/account/appointments`.
6. `POST /api/booking/slots` для обох режимів (`masterId` заданий / `null`) → 200, форма відповіді незмінна.
7. `POST /api/booking/create` + `/api/booking/confirm` — повний цикл, включно з `Serializable`-транзакцією.
8. Клієнт: `npm run dev` → головна, `/booking`, `/cabinet`, `/admin` рендеряться без помилок у консолі.

### 6.4. Відкат

Відкат змін Фази 0. БД не зачіпається — відкат чистий.

### 6.5. Ризик поза .NET

`csharp-ls`, налаштований у `~/.kiro/settings/lsp.json`, може не підтримувати `net10.0`. **Не перевірено.** Якщо після апгрейду зникне діагностика по `.cs` — це проблема LSP, не проєкту; вирішується окремо.

**Оцінка:** 2–3 год (більше, якщо спрацює п. 6.2 №1 або №4).


---

## 7. Фаза 1 — EF Migrations через baseline

**Мета:** отримати керовану схему без втрати даних.
**Ризик:** зачіпає живу БД з даними. **Виконувати лише після явного підтвердження.**

### 7.1. Бекап (обов'язково)

```powershell
docker exec beauty-db pg_dump -U beauty -d beauty > db-backup-2026-09-03.sql
```

Файл покласти поза проєктом. Перевірити розмір > 0.

### 7.2. Генерація `InitialCreate`

```powershell
dotnet ef migrations add InitialCreate --project server
```

Створює `server/Migrations/<ts>_InitialCreate.cs`, `<ts>_InitialCreate.Designer.cs`, `AppDbContextModelSnapshot.cs`.
`server/Migrations/` **зберігається** разом із кодом.

### 7.3. Звірка з фактичною схемою — критичний крок

```powershell
dotnet ef migrations script --project server --output baseline-check.sql
```

Порівняти зі станом БД (`\d "<таблиця>"` у psql) для кожної з 8 таблиць. Чек-лист:

| Об'єкт | Що перевірити |
|---|---|
| `Users` | `Phone` — **unique index**; `Role` як integer; `CreatedAt` як `timestamp with time zone` |
| `Services` | `Price` як `numeric`; `Category` як `text`; `IsActive` як boolean |
| `Masters` | `RotationType` як **integer** (не text!); `RotationAnchor` як **date** |
| `MasterServices` | **композитний PK** `(MasterId, ServiceId)`; FK на обидві таблиці |
| `WorkingHours` | `DayOfWeek` як integer; `Start`/`End` як **`time without time zone`** |
| `Appointments` | індекс `(MasterId, StartTime)`; `Status`, `NotifyChannel` як integer; `StartTime`/`EndTime`/`CreatedAt` як `timestamptz`; `ReminderSentAt` nullable |
| `VerificationCodes` | `Channel` як integer; `AppointmentId` nullable; `ConsumedAt` nullable |
| `NotificationLogs` | `Success`, `IsMock` як boolean |

**Якщо є розбіжність:** джерело істини — **фактична БД**. Правити згенеровану міграцію під неї, потім додатково перевірити читання/запис `WorkingHours` (`TimeOnly`) і `Master.RotationAnchor` (`DateOnly`) через API. Причина можливої розбіжності — див. 6.2, невідома #4.

Через зміну в CLI (вивід у stderr) перевірити, що `baseline-check.sql` не порожній.

### 7.4. Baseline-позначка

DDL таблиці історії взяти **з початку `baseline-check.sql`** (не писати з голови — провайдер задає точний тип колонок), виконати його, потім:

```sql
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('<ts>_InitialCreate', '10.0.11');
```

`<ts>_InitialCreate` — точна назва з імені файлу міграції. `ProductVersion` — версія EF з `.csproj`.

Контроль: `dotnet ef migrations list --project server` показує `InitialCreate` як застосовану.

### 7.5. `Migrate()` і `DbInitializer`

За фіксом **C7** з `01-backend.md` сід виноситься з `AppDbContext` в окремий клас:

1. Створити `server/Data/DbInitializer.cs`: перенести тіло `AppDbContext.Seed(db, cfg)` **без зміни семантики** (баги D8 не чіпаємо в цій фазі — інакше змішаємо рефакторинг із виправленням даних).
2. Перший рядок: `db.Database.Migrate()` замість `db.Database.EnsureCreated()`.
3. Ідемпотентність зберегти: умови `!db.Users.Any(u => u.Role == UserRole.Admin)` і `!db.Services.Any()`.
4. `AppDbContext.Seed` видалити; у `Program.cs` замінити виклик на `DbInitializer.Seed(db, app.Configuration)`.

### 7.6. Критерій приймання

1. `dotnet ef migrations list` → `InitialCreate` (applied).
2. `dotnet run --project server` → стартує, у БД **не з'явилось дублів** послуг/майстрів/адміна (`SELECT count(*) FROM "Services"` = 6, `"Masters"` = 6).
3. Дані до і після — без змін: `SELECT count(*) FROM "Appointments"`, `"Users"`.
4. `GET /api/catalog/masters` → 200 з тими самими даними.

### 7.7. Відкат

- Код: відкат змін.
- БД: `DROP TABLE "__EFMigrationsHistory";` — схема лишається як була, `EnsureCreated()` знову працює.
- У найгіршому разі — відновлення з дампа 7.1.

**Оцінка:** 1,5–2 год.

---

## 8. Фаза 2 — Поле `PhotoUrl`

### 8.1. Модель

`server/Models/Entities.cs`, клас `Master`, після `public bool IsActive`:

```csharp
/// <summary>Відносний шлях до фото, напр. /uploads/masters/3-a1b2c3d4.webp. null → показуємо ініціали.</summary>
public string? PhotoUrl { get; set; }
```

Зберігаємо **відносний** шлях, не абсолютний URL: зміна домену чи порту не має ламати посилання.

### 8.2. Міграція

```powershell
dotnet ef migrations add AddMasterPhotoUrl --project server
dotnet ef database update --project server
```

Очікуваний DDL: `ALTER TABLE "Masters" ADD "PhotoUrl" text NULL;`
Перевірка: `\d "Masters"` → `PhotoUrl | text | YES`.

### 8.3. Віддача поля

`server/Controllers/CatalogController.cs`, метод `Masters()` — додати `m.PhotoUrl` до анонімного об'єкта (після `m.Bio`).

`server/Controllers/AdminController.cs`, метод `Masters()` — те саме.

### 8.4. Запис поля

`server/Controllers/AdminController.cs`, record `MasterDto` — новий параметр **останнім**:

```csharp
public record MasterDto(string Name, string? Specialty, string? Bio, bool IsActive, string RotationType,
    DateOnly RotationAnchor, List<int> ServiceIds, List<WhDto> WorkingHours, string? PhotoUrl);
```

Позиційний record: додавання в середину зламає біндинг. Присвоєння в `AddMaster` і `UpdateMaster`:
`m.PhotoUrl = dto.PhotoUrl;`

**Увага:** `Admin.tsx` шле body з полем `photoUrl` лише після Фази 10. До того `dto.PhotoUrl` буде `null` → `UpdateMaster` **обнулить** уже завантажене фото. Тому:
- або Фаза 10 виконується одразу після Фази 3 (рекомендовано);
- або в `UpdateMaster` тимчасово `if (dto.PhotoUrl != null) m.PhotoUrl = dto.PhotoUrl;`.
**Рішення:** використати умовне присвоєння `if (dto.PhotoUrl != null)` **назавжди** — обнулення робиться окремим `DELETE`-ендпоінтом (8.3 Фази 3), а не порожнім полем у формі. Це прибирає клас помилок «зберіг форму — втратив фото».

### 8.5. Критерій приймання

1. Колонка існує, nullable.
2. `GET /api/catalog/masters` і `GET /api/admin/masters` повертають `"photoUrl": null`.
3. `PUT /api/admin/masters/{id}` зі старим body (без `photoUrl`) — не падає, фото не змінюється.

**Оцінка:** 30 хв.

---

## 9. Фаза 3 — Upload фото: конфіг, статика, ендпоінти, безпека

### 9.1. Конфігурація шляху

`server/appsettings.json`, новий блок після `"Salon"`:

```json
"Uploads": {
  "MastersPath": "wwwroot/uploads/masters",
  "MaxFileSizeBytes": 3145728
}
```

Хардкоду шляху в коді немає — читати через `IConfiguration`. Це робить майбутній переїзд у контейнер (D2) зміною одного рядка.

### 9.2. Статика

`server/Program.cs`, після `app.UseCors()` і **до** `app.MapControllers()`:

```csharp
app.UseStaticFiles();
```

Створити папку `server/wwwroot/uploads/masters` (якщо відсутня).

Завантажені фото й дампи БД зберігаються локально, поза межами проєкту.

Обґрунтування: `03-infra-qa.md` §3.5 уже фіксує проблему великих бінарників у проєкті (11 PNG ≈ 25 МБ) — завантажені фото тримаємо окремо, поза проєктом.

### 9.3. Ендпоінт завантаження

`server/Controllers/AdminController.cs`. Клас уже має `[Authorize(Roles = "Admin")]` — успадковується. Інжектувати `IWebHostEnvironment env` і `IConfiguration cfg` у primary constructor.

**Контракт:**

```
POST /api/admin/masters/{id}/photo
Content-Type: multipart/form-data
Поле форми: file
```

| Код | Тіло | Умова |
|---|---|---|
| 200 | `{ "photoUrl": "/uploads/masters/3-a1b2c3d4.webp" }` | успіх |
| 400 | `{ "error": "Файл не вибрано" }` | `file == null \|\| file.Length == 0` |
| 400 | `{ "error": "Розмір файлу перевищує 3 МБ" }` | `file.Length > MaxFileSizeBytes` |
| 400 | `{ "error": "Дозволені лише файли JPG, PNG або WebP" }` | розширення не у whitelist |
| 400 | `{ "error": "Вміст файлу не є зображенням JPG, PNG або WebP" }` | magic bytes не збігаються |
| 401/403 | — | не адмін |
| 404 | `{ "error": "Майстра не знайдено" }` | немає такого `id` |

**Обов'язкові вимоги безпеки:**

1. `[RequestSizeLimit(3 * 1024 * 1024)]` на методі **плюс** перевірка `file.Length` у коді (атрибут захищає pipeline, перевірка дає зрозумілу помилку).
2. Whitelist розширень: `.jpg`, `.jpeg`, `.png`, `.webp` — порівняння через `Path.GetExtension(...).ToLowerInvariant()`.
3. **`file.ContentType` від клієнта не довіряти** — його підставляє браузер.
4. Перевірка magic bytes першими байтами потоку:
   - JPEG: `FF D8 FF`
   - PNG: `89 50 4E 47 0D 0A 1A 0A`
   - WebP: `52 49 46 46` (`RIFF`) на офсеті 0 **і** `57 45 42 50` (`WEBP`) на офсеті 8
   Прочитати перші 12 байт, потім `stream.Position = 0` перед копіюванням.
5. **Ім'я файлу генерує сервер:** `$"{id}-{Guid.NewGuid():N}{ext}"`. `file.FileName` у шляху не використовується **взагалі** — це вектор path traversal.
6. Шлях: `Path.Combine(env.ContentRootPath, cfg["Uploads:MastersPath"]!, name)`; `Directory.CreateDirectory(...)` для батьківської папки.
7. Старий файл видаляти **після** успішного запису нового, у `try/catch` — помилка видалення не валить запит (лише лог).
8. Записати `m.PhotoUrl = $"/uploads/masters/{name}"` (з провідним слешем), `SaveChangesAsync`, повернути `{ photoUrl }`.

Дефолтний ліміт тіла запиту ASP.NET Core (≈30 МБ) наш 3 МБ не перевищує → глобальних налаштувань Kestrel не потрібно.

### 9.4. Ендпоінт видалення

```
DELETE /api/admin/masters/{id}/photo → 200 { "ok": true }
```

Видалити файл (якщо існує), `m.PhotoUrl = null`. 404, якщо майстра немає. Якщо фото не було — теж 200 (ідемпотентність).

### 9.5. Каскад при видаленні майстра

`AdminController.DeleteMaster`: у гілці **hard-delete** (майстер без записів) — видалити файл фото перед `db.Masters.Remove(m)`.
У гілці **soft-delete** — файл **лишити**: майстра можуть повернути, а історія записів імені майстра не втрачає.

### 9.6. Клієнтський доступ до статики

`client/.env` містить `VITE_API_URL=http://localhost:5000`, тобто запити йдуть напряму, не через проксі. Отже:

- **Основний шлях:** `resolvePhotoUrl` склеює `VITE_API_URL` з відносним шляхом → `http://localhost:5000/uploads/masters/...`. Працює без змін у Vite.
- **Додатково** (для випадку, коли `VITE_API_URL` порожній і використовується проксі) у `client/vite.config.ts` додати:
  ```ts
  server: { port: 5173, proxy: { '/api': 'http://localhost:5000', '/uploads': 'http://localhost:5000' } }
  ```
  Це страховка, а не основний механізм.

**CORS не потрібен** для `<img src>` — теги зображень не підпадають під CORS-обмеження при простому відображенні.

### 9.7. Процедура бекапу (D1)

Додати в `README.md` розділ «Бекап»:

> Бекап складається з **двох** частин і робиться разом:
> 1. `docker exec beauty-db pg_dump -U beauty -d beauty > backup.sql`
> 2. архів папки `server/wwwroot/uploads`
>
> `pg_dump` **не** містить файлів фото — у БД лише шляхи. Втрата папки дає биті посилання; UI не ламається (працює fallback на ініціали), але фото зникають безповоротно.

### 9.8. Критерій приймання

1. Upload `.jpg`, `.png`, `.webp` до 3 МБ → 200, файл з'явився в папці, `PhotoUrl` у БД, фото відкривається за `http://localhost:5000/uploads/masters/<name>`.
2. Файл 5 МБ → 400 з текстом про розмір.
3. `.txt`, перейменований у `.jpg` → 400 з текстом про вміст.
4. `.exe` → 400 з текстом про розширення.
5. Повторний upload → старий файл зник з папки, новий на місці.
6. `DELETE .../photo` → файл зник, `PhotoUrl = null`.
7. Запит без токена адміна → 401/403.
8. Файл `../../../appsettings.json` у полі `file` (через curl з підміною `filename`) → зберігається під згенерованим ім'ям у цільовій папці, нічого поза нею не створюється.

### 9.9. Відкат

Відкат змін + `UPDATE "Masters" SET "PhotoUrl" = NULL;` + ручне очищення папки. Колонку лишити (вона nullable і нікому не шкодить).

**Оцінка:** 2,5 год.


---

## 10. Фаза 4 — `ScheduleService`: єдине джерело правил графіка

**Мета:** одна реалізація правил (ротація, робочі години, зайнятість, вікно 14 днів) для всіх споживачів: публічні слоти, графік майстра, прев'ю ротації в адмінці, валідація переназначення.
**Виконує роадмап P1-2** (`BookingValidator`) — див. R12. Назву фіксуємо як `ScheduleService`, роадмап оновити.

### 10.1. Новий файл `server/Services/ScheduleService.cs`

DTO (у тому ж файлі або `server/Models/ScheduleDtos.cs`):

```csharp
public record SlotMasterDto(int Id, string Name);
public record SlotDto(DateTime Start, DateTime End, List<SlotMasterDto>? Masters);
public record BusyIntervalDto(DateTime Start, DateTime End);
public record MasterDayDto(DateOnly Date, bool IsWorking, TimeOnly? Start, TimeOnly? End, List<BusyIntervalDto> Busy);
public record BookingCheck(bool Ok, string? Error);
```

Публічний API сервісу:

```csharp
public class ScheduleService(AppDbContext db, SalonClock clock)
{
    /// <summary>Глибина вікна бронювання: поточний + наступний тиждень.</summary>
    public const int WindowDays = 14;

    /// <summary>Вільні слоти. masterId == null → усі активні майстри послуги,
    /// кожен слот містить список вільних майстрів.</summary>
    public Task<List<SlotDto>> GetSlotsAsync(int? masterId, int serviceId, CancellationToken ct = default);

    /// <summary>Графік майстра на 14 днів без прив'язки до послуги:
    /// робочі дні, години, зайняті інтервали (без даних клієнтів).</summary>
    public Task<List<MasterDayDto>> GetMasterScheduleAsync(int masterId, CancellationToken ct = default);

    /// <summary>Прев'ю ротації для довільних параметрів, без звернення до БД.
    /// Використовується у формі адмінки для ще не збереженого майстра.</summary>
    public List<MasterDayDto> PreviewRotation(RotationType type, DateOnly anchor);

    /// <summary>Чи можна поставити запис майстру на цей час.
    /// ignoreAppointmentId — щоб запис не конфліктував сам із собою при переназначенні.</summary>
    public Task<BookingCheck> ValidateAsync(int masterId, int serviceId, DateTime startUtc,
        int? ignoreAppointmentId = null, CancellationToken ct = default);
}
```

Реєстрація в `Program.cs`: `builder.Services.AddScoped<ScheduleService>();`

### 10.2. Правила, які сервіс реалізує (єдине місце)

1. Вікно: від `clock.TodayInSalon` до `+WindowDays` включно.
2. Робочий день: `RotationHelper.WorksOn(master, day)`.
3. Робочі години: `master.WorkingHours.FirstOrDefault(w => w.DayOfWeek == day.DayOfWeek)`; пропустити, якщо `null` або `End <= Start`.
4. Крок сітки — **30 хв**; слот вміщується, якщо `t.AddMinutes(service.DurationMin) <= wh.End`.
5. Конвертація «настінного» часу в UTC — **лише** `clock.ToUtc(day, t)`.
6. Слот у минулому не показується: `startUtc > DateTime.UtcNow`.
7. Майстер має бути `IsActive` **і** мати цю послугу через `MasterServices`.
8. **Виправлення D4:** послуга має бути `IsActive` — інакше 404/помилка.
9. **Виправлення D3:** зайнятість вважається однаково для слотів і валідації:
   `Status == Confirmed` **АБО** (`Status == PendingVerification` **І** `CreatedAt > UtcNow.AddMinutes(-15)`).
   Раніше `Slots` враховував лише `Confirmed`, а `Create` — обидва, через що UI показував вільний час, а API повертав 409.
10. Перетин: `startUtc < existing.EndTime && endUtc > existing.StartTime`.

### 10.3. Виправлення дефектів графіка (входять у цю фазу)

**D2 — `server/Data/RotationHelper.cs:18`.** Weekly-гілка:

```csharp
// було: ((MondayOf(day).DayNumber - master.RotationAnchor.DayNumber) / 7) % 2 == 0
// стало: нормалізований залишок, коректний і для дат до якоря
_ => (((MondayOf(day).DayNumber - master.RotationAnchor.DayNumber) / 7) % 2 + 2) % 2 == 0
```

Ділення цілих у C# відкидає в бік нуля, тому для дат **до** якоря вираз давав від'ємний залишок і `!= 0` для тижнів, які насправді робочі. `TwoTwo`-гілка вже нормалізована (`((x % 4) + 4) % 4`) — не чіпати.

**D1 — `server/Controllers/CatalogController.cs:20`.** Інжектувати `SalonClock clock` у контролер і замінити:

```csharp
// було: var today = DateOnly.FromDateTime(DateTime.UtcNow);
var today = clock.TodayInSalon;
```

Без цього о 23:30 Kyiv (20:30 UTC зимою) прапорці `worksToday`/`worksTomorrow` на головній показують попередній день.

**Не входить:** D8 (seed) і D11 (плаваючий якір) — вони змінюють **дані**, а не логіку; окремі задачі.

### 10.4. Переписати `BookingController`

1. Інжектувати `ScheduleService schedule`.
2. `Slots` → делегує в `schedule.GetSlotsAsync(q.MasterId, q.ServiceId, ct)` і лише формує JSON.
   **Форму відповіді зберегти точно** — `Booking.tsx` на неї спирається:
   - `masterId` задано → `[{ start, end }]` (без `masters`);
   - `masterId == null` → `[{ start, end, masters: [{ id, name }] }]`.
3. У `Create` замінити ручні перевірки на `schedule.ValidateAsync(...)`, зберігши:
   - транзакцію `BeginTransactionAsync(IsolationLevel.Serializable)`;
   - тексти помилок 1:1 (`«Час поза доступним вікном бронювання»`, `«Майстер не працює цього дня»`, `«Час поза робочими годинами майстра»`, `«Цей час уже зайнятий або очікує підтвердження»`);
   - коди: 400 для правил, 409 для зайнятості, 404 для відсутніх сутностей.
4. Константу `BookingWindowDays` видалити, замінити на `ScheduleService.WindowDays` (використання: рядки 33 і 113).
5. Додати `CancellationToken` у сигнатури нових/змінених методів (рекомендація §1.4 рев'ю).

### 10.5. Контракт часу для нових ендпоінтів (D9)

Усі **нові** ендпоінти, що приймають час, вимагають **ISO-8601 з `Z`** (UTC): `"2026-09-05T07:00:00Z"`.
Клієнт бере значення прямо з `slot.start`, який прийшов від API, — не конструює час рядками.
Наявний `AdminController.Reschedule` з `SpecifyKind(..., Utc)` **не чіпаємо** — це задача C3.

### 10.6. Критерій приймання (регресія — найважливіше в цій фазі)

1. `POST /api/booking/slots` з `masterId` → відповідь **байт-у-байт** тієї ж форми, що до рефакторингу (порівняти збережені JSON до/після).
2. Те саме для `masterId: null` — включно з масивом `masters` і сортуванням за `start`.
3. `Booking.tsx` працює в обох режимах: вибір послуги → «будь-який майстер» → слоти → вибір майстра зі слота → бронювання → код → підтвердження.
4. Той самий цикл у режимі «обрати майстра».
5. **Новий тест D3:** створити `PendingVerification`-запис і не підтверджувати. Слот, який він займає, **не показується** в `slots` протягом 15 хв (раніше показувався і давав 409).
6. **Новий тест D4:** деактивувати послугу (`IsActive = false`) → `slots` для неї повертає 404/помилку, а не порожній масив із 200.
7. **Новий тест D2:** майстер `Weekly` з якорем у майбутньому (напр. `+14 днів`) — робочі тижні в поточному періоді визначаються коректно.
8. Слот у минулому відсутній; слот на 15-й день відсутній.

**Оцінка:** 2 год.

---

## 11. Фаза 5 — API сторінки майстра

### 11.1. `GET /api/catalog/masters/{id}`

`[AllowAnonymous]` (явно, за конвенцією 4.4).

**200:**

```json
{
  "id": 1,
  "name": "Оксана",
  "specialty": "Перукар",
  "bio": "Жіночі та чоловічі стрижки, досвід 8 років",
  "photoUrl": "/uploads/masters/1-a1b2c3d4.webp",
  "rotationType": "TwoTwo",
  "rotationAnchor": "2026-09-03",
  "worksToday": true,
  "worksTomorrow": false,
  "worksThisWeek": true,
  "worksNextWeek": true,
  "services": [
    { "id": 1, "name": "Стрижка чоловіча класична", "category": "hair", "durationMin": 30, "price": 300 }
  ]
}
```

**404:** `{ "error": "Майстра не знайдено" }` — якщо немає такого `id` **або** `IsActive == false`.

Правила:
- `services` — лише **активні** послуги майстра (`Service.IsActive`), відсортовані `Category`, потім `Name` (як у `CatalogController.Services()`).
- Прапорці `worksToday`/`worksTomorrow`/`worksThisWeek`/`worksNextWeek` — за тією ж логікою, що в `Masters()`, але через `clock.TodayInSalon` (D1).
- `rotationAnchor` серіалізується як `"YYYY-MM-DD"` (дефолт `DateOnly` у System.Text.Json).

C#-типи:

```csharp
public record MasterServiceBriefDto(int Id, string Name, string Category, int DurationMin, decimal Price);
public record MasterDetailDto(int Id, string Name, string? Specialty, string? Bio, string? PhotoUrl,
    string RotationType, DateOnly RotationAnchor,
    bool WorksToday, bool WorksTomorrow, bool WorksThisWeek, bool WorksNextWeek,
    List<MasterServiceBriefDto> Services);
```

### 11.2. `GET /api/catalog/masters/{id}/schedule`

`[AllowAnonymous]`. Делегує в `schedule.GetMasterScheduleAsync(id, ct)`.

**200:**

```json
[
  {
    "date": "2026-09-03",
    "isWorking": true,
    "start": "09:00:00",
    "end": "19:00:00",
    "busy": [ { "start": "2026-09-03T07:00:00Z", "end": "2026-09-03T07:30:00Z" } ]
  },
  { "date": "2026-09-04", "isWorking": false, "start": null, "end": null, "busy": [] }
]
```

- Рівно `WindowDays + 1` елементів (сьогодні + 14 днів), впорядковано за `date`.
- `start`/`end` — `TimeOnly` → `"HH:mm:ss"` (формат підтверджений наявним кодом: `Admin.tsx` робить `.slice(0, 5)` над цими значеннями).
- `busy` — **лише інтервали**, у UTC. **Заборонено** повертати ім'я клієнта, телефон, назву послуги — це публічний ендпоінт.
- `busy` включає `Confirmed` і свіжі `PendingVerification` (правило 10.2 №9), щоб графік не суперечив слотам.

**404:** `{ "error": "Майстра не знайдено" }`.

### 11.3. Критерій приймання

1. Обидва ендпоінти доступні **без** токена.
2. Неактивний майстер → 404 на обох.
3. `services` не містить деактивованих послуг.
4. `busy` не містить полів `client`, `phone`, `service` (перевірити сирий JSON).
5. Для майстра `TwoTwo` рівно половина днів у вікні `isWorking: false`, і вони чергуються по 2.
6. Для `Weekly` — тижневе чергування, коректне і при якорі в майбутньому (D2).
7. Кількість елементів = 15.

**Оцінка:** 1 год.


---

## 12. Фаза 6 — Клієнт: типи та API-обгортки

### 12.1. `client/src/types.ts`

**Змінити наявний `MasterDto`** — додати фото і привести опційність до реальної відповіді сервера. Зараз тип оголошує `isActive`, `rotationAnchor`, `workingHours` як обов'язкові, але `/api/catalog/masters` їх **не повертає** (їх віддає лише `/api/admin/masters`) — це джерело помилок і причина маскування через `?.` (§2.2 рев'ю):

```ts
export interface MasterDto {
  id: number
  name: string
  specialty?: string | null
  bio?: string | null
  photoUrl?: string | null
  /** Повертає лише /api/admin/masters. */
  isActive?: boolean
  rotationType: RotationType
  /** Повертає лише /api/admin/masters. */
  rotationAnchor?: string
  services: number[]
  /** Повертає лише /api/admin/masters. */
  workingHours?: WorkingHourDto[]
  worksThisWeek?: boolean
  worksNextWeek?: boolean
  worksToday?: boolean
  worksTomorrow?: boolean
}
```

`services` лишається `number[]` — сервер віддає масив id (`m.MasterServices.Select(ms => ms.ServiceId)`); `?.` у `Booking.tsx:78` після цього можна прибрати (`m.services.includes(...)`).

**Додати нові типи:**

```ts
export interface MasterServiceBriefDto {
  id: number
  name: string
  category: 'hair' | 'manicure'
  durationMin: number
  price: number
}

export interface MasterDetailDto {
  id: number
  name: string
  specialty?: string | null
  bio?: string | null
  photoUrl?: string | null
  rotationType: RotationType
  /** YYYY-MM-DD */
  rotationAnchor: string
  worksToday: boolean
  worksTomorrow: boolean
  worksThisWeek: boolean
  worksNextWeek: boolean
  services: MasterServiceBriefDto[]
}

export interface BusyIntervalDto {
  /** ISO-8601 UTC */
  start: string
  end: string
}

export interface MasterScheduleDayDto {
  /** YYYY-MM-DD (дата в таймзоні салону) */
  date: string
  isWorking: boolean
  /** HH:mm:ss або null для вихідного */
  start?: string | null
  end?: string | null
  busy: BusyIntervalDto[]
}

export interface RotationPreviewDayDto {
  date: string
  isWorking: boolean
}
```

**`MasterForm`** — додати `photoUrl: string` (для прев'ю в адмінці).

### 12.2. `client/src/api.ts`

Додати до типізованих обгорток:

```ts
export const fetchMaster = (id: number) => api<MasterDetailDto>(`/api/catalog/masters/${id}`)
export const fetchMasterSchedule = (id: number) => api<MasterScheduleDayDto[]>(`/api/catalog/masters/${id}/schedule`)
export const deleteMasterPhoto = (id: number) => api<{ ok: boolean }>(`/api/admin/masters/${id}/photo`, { method: 'DELETE' })
export const previewRotation = (body: { rotationType: RotationType; rotationAnchor: string }) =>
  api<RotationPreviewDayDto[]>('/api/admin/rotation-preview', { method: 'POST', body })
```

**Хелпер шляху до фото:**

```ts
/** Абсолютний URL фото. Сервер віддає відносний шлях (/uploads/...),
 *  а статика лежить на бекенді, тому склеюємо з API-хостом. */
export function resolvePhotoUrl(path?: string | null): string | null {
  if (!path) return null
  if (/^https?:\/\//i.test(path)) return path
  return API + path
}
```

`API` — уже наявна модульна константа (`import.meta.env.VITE_API_URL || ''`).

**Upload — окрема функція, бо `api()` завжди ставить `Content-Type: application/json`:**

```ts
/** Завантаження фото майстра. Content-Type НЕ ставимо — browser додасть
 *  multipart boundary сам. */
export async function uploadMasterPhoto(id: number, file: File): Promise<{ photoUrl: string }> {
  const fd = new FormData()
  fd.append('file', file)
  const res = await fetch(`${API}/api/admin/masters/${id}/photo`, {
    method: 'POST',
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: fd
  })
  const data = await res.json().catch(() => ({})) as { error?: string; photoUrl?: string }
  if (!res.ok) throw new Error(data.error || `Помилка ${res.status}`)
  return { photoUrl: data.photoUrl! }
}
```

Ім'я поля `'file'` мусить збігатися з іменем параметра `IFormFile file` у контролері.

### 12.3. Критерій приймання

`npm run build` (tsc) без помилок.

**Оцінка:** 40 хв.

---

## 13. Фаза 7 — Компонент `MasterCard`

### 13.1. Новий файл `client/src/components/MasterCard.tsx`

Папки `components/` ще немає — створюємо (цільова структура зафіксована в `02-frontend.md` §2.2).

Вимоги:

1. **Корінь — `<Link>`, не `<div onClick>`:**
   ```tsx
   <Link to={`/masters/${m.id}`} className="card master-card">
   ```
   Дає клікабельність усієї картки, нативну навігацію з клавіатури і `Ctrl+click` у новій вкладці без додаткового коду й ARIA.
2. **Фото:**
   ```tsx
   <img className="master-photo" src={photo} alt={`Фото майстра ${m.name}`}
        loading="lazy" width={240} height={240} onError={() => setFailed(true)} />
   ```
   `width`/`height` обов'язкові — проти CLS. `alt` описовий, не порожній.
3. **Fallback** при `!photoUrl` або `onError` — коло з ініціалами:
   ```tsx
   <div className="master-photo master-photo--fallback" aria-hidden="true">{initials}</div>
   ```
   `initials` = перша літера імені у верхньому регістрі (імена в проєкті односкладові: «Оксана», «Ірина»). Елемент декоративний → `aria-hidden`, бо ім'я вже є текстом нижче.
4. **Текст картки** — перенести з `Home.tsx` **без зміни логіки**: `<h3>{m.name}</h3>`, `<p>{m.specialty}</p>`, `<p className="muted">{m.bio}</p>` і наявний тернарник бейджа графіка:
   ```tsx
   {m.rotationType === 'TwoTwo'
     ? (m.worksToday ? '✅ На зміні сьогодні' : m.worksTomorrow ? '🕘 На зміні з завтра' : '🏖 Зараз вихідні (2/2)')
     : <>{m.worksThisWeek ? '✅ Працює цього тижня' : '🏖 Відпочиває цього тижня'}{' · '}
        {m.worksNextWeek ? '✅ працює наступного' : '🏖 відпочиває наступного'}</>}
   ```
5. **Афорданс:** рядок-підказка `<span className="master-card__cta">Графік і запис →</span>`, щоб клікабельність була очевидною.
6. **D12 (контраст):** текст поверх фото не розміщувати; підписи під фото — на `--text`, `--muted` лишити тільки для `bio`. `--text-dim` (`rgba(255,255,255,.6)`) для нового тексту **не використовувати**.

### 13.2. `client/src/pages/Home.tsx`

1. У секції «Майстри» замінити інлайн `<div className="card">…</div>` на `<MasterCard key={m.id} m={m} />`.
2. **D7 (мінімальний фікс):** додати `loading` і `catch`:
   ```tsx
   const [loading, setLoading] = useState(true)
   const [err, setErr] = useState('')
   useEffect(() => {
     let alive = true
     Promise.all([fetchServices(), fetchMasters()])
       .then(([s, m]) => { if (alive) { setServices(s); setMasters(m) } })
       .catch(e => { if (alive) setErr(errMsg(e)) })
       .finally(() => { if (alive) setLoading(false) })
     return () => { alive = false }
   }, [])
   ```
   Прапорець `alive` замість `AbortController` — мінімальна правка проти setState після unmount; повний `AbortController` — задача F-Major-13 у роадмапі.
3. Показувати `{err && <p className="error">{err}</p>}` і `{loading && <p className="muted">Завантаження…</p>}`. Секцію «Послуги» не переписувати.

### 13.3. `client/src/styles.css` — тільки доповнення

Додати в кінець файлу (нічого наявного не змінювати):

```css
/* ---------- Картка майстра ---------- */
a.master-card { display: block; color: inherit; text-decoration: none; transition: transform .2s, border-color .2s; }
a.master-card:hover { transform: translateY(-2px); border-color: var(--glass-30); }
a.master-card:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.master-photo {
  display: block; width: 100%; aspect-ratio: 1 / 1; object-fit: cover;
  border-radius: var(--radius); margin-bottom: 12px; background: var(--glass-10);
}
.master-photo--fallback {
  display: flex; align-items: center; justify-content: center;
  font-size: 48px; font-weight: 700; color: var(--accent);
  border: 1px solid var(--glass-15);
}
.master-card__cta { display: block; margin-top: 10px; color: var(--accent); font-size: 14px; }
```

Сітка `.grid` уже адаптивна (`repeat(auto-fill, minmax(240px, 1fr))`) — змін не потребує.

### 13.4. Критерій приймання

1. Майстер з фото — фото видно, пропорції 1:1, без обрізання по центру обличчя (перевірити візуально).
2. Майстер без фото — коло з ініціалом.
3. Битий `photoUrl` (вручну зіпсувати в БД) → fallback, у консолі немає незловлених помилок.
4. Клік будь-де по картці → `/masters/{id}`.
5. `Tab` до картки → видима рамка фокуса; `Enter` переходить.
6. Ширина 320px — картки в одну колонку, фото не ламає верстку.
7. Бекенд вимкнено → на головній видно повідомлення про помилку, а не порожня сторінка.

**Оцінка:** 1,5 год.

---

## 14. Фаза 8 — Сторінка `/masters/:id`

### 14.1. Роут

`client/src/App.tsx`:

```tsx
import MasterSchedule from './pages/MasterSchedule'
...
<Route path="/masters/:id" element={<MasterSchedule />} />
```

Ставити **після** `/booking`, до `/login` — порядок у react-router v6 не важливий, але тримаємо логічне групування. `<ProtectedRoute>`/`NotFound` — задача F-Critical-11, тут не робимо; сторінка публічна.

### 14.2. Новий файл `client/src/pages/MasterSchedule.tsx`

**Структура (порядок кроків за R4):**

1. **Завантаження.** `const { id } = useParams()`; `Number(id)`; якщо `NaN` → блок «Майстра не знайдено».
   Паралельно `Promise.all([fetchMaster(mid), fetchMasterSchedule(mid)])`, стани `loading` / `err` / дані. Прапорець `alive` як у 13.2.
2. **Шапка:** фото (та сама розмітка й fallback, що в `MasterCard` — виділити спільний `MasterPhoto` або продублювати 5 рядків; **рішення: виділити** `client/src/components/MasterPhoto.tsx`, щоб fallback і `onError` не розходились), ім'я `<h2>`, спеціальність, `bio`, тип графіка словами («2 дні через 2» / «тиждень через тиждень»).
3. **Календар робочих днів на 14 днів** — видно **одразу**, до вибору послуги (це і є «графік роботи» із запиту користувача). Компонент `BookingCalendar` (§14.6) у режимі `mode="schedule"`:
   - джерело — `schedule` з `GET /api/catalog/masters/{id}/schedule`: дата доступна, якщо `isWorking === true`;
   - вихідні дні майстра — сірі, `disabled`, з `title="Вихідний"`;
   - під календарем — легенда і робочі години дня (`start`–`end`), коли дату обрано;
   - для обраної дати показати зайняті інтервали приглушено (`.slot--busy`), щоб було видно завантаженість ще до вибору послуги;
   - формат підписів — `Intl.DateTimeFormat('uk-UA', { timeZone: 'Europe/Kyiv', ... })`. **Не** використовувати `new Date(day + 'T12:00')` без зони — заборонений хак (§2.5 рев'ю);
   - час інтервалів — `toLocaleTimeString('uk-UA', { timeZone: 'Europe/Kyiv', hour: '2-digit', minute: '2-digit' })`.
4. **Крок 1 «Оберіть послугу»** — кнопки `.slot` зі `master.services`: назва, тривалість, ціна. Вибір → `setServiceId` і `fetchSlots(mid, serviceId)`.
5. **Крок 2 «Оберіть дату»** — той самий `BookingCalendar` переходить у режим `mode="booking"`:
   - тепер дата доступна лише якщо `isWorking` **і** на неї є хоча б один слот (день може бути робочим, але повністю зайнятим, або вільних 30 хв не хватає на 120-хвилинну послугу);
   - дати робочі, але без вільного часу — `disabled` з `title="Немає вільного часу"`;
   - раніше обрана дата зберігається, якщо вона лишилась доступною; інакше скидається.
6. **Крок 3 «Оберіть час»** — слоти **тільки обраної дати**, кнопки `.slot` / `.slot.sel`.
   Поки слоти вантажаться — `disabled` і текст «Завантаження…».
   Порожній результат по всьому вікну → «У цього майстра немає вільного часу на цю послугу в найближчі 14 днів».
7. **Крок 4 «Забронювати»** — кнопка `.btn.primary`:
   ```tsx
   navigate('/booking', { state: { masterId: mid, serviceId, startTime: slot } })
   ```
   `slot` — рядок `start` **як прийшов від API** (ISO з `Z`), без переформатування (D9).
8. Редирект неавторизованого **не дублювати** — він уже є в `Booking.create()`.

### 14.3. `client/src/pages/Booking.tsx` — календар + читання `location.state`

Дві зміни: (а) вибір часу стає двоетапним через `BookingCalendar`, (б) підхват prefill зі сторінки майстра.

#### 14.3.1. Календар замість списку всіх днів

Зараз (`Booking.tsx:38-46` і `120-140`) усі слоти на 14 днів показуються одним списком через `useMemo` з `s.start.slice(0, 10)`. Замінюємо:

1. **Групування по салонній даті.** `slice(0, 10)` дає **UTC**-дату, а не дату салону — це заборонений хак (§2.5 рев'ю). Правильно:
   ```tsx
   // 'sv-SE' дає формат YYYY-MM-DD; timeZone переводить у дату салону
   const salonDate = (iso: string) =>
     new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Kyiv' }).format(new Date(iso))

   const byDate = useMemo(() => {
     const map = new Map<string, SlotDto[]>()
     slots.forEach(s => {
       const d = salonDate(s.start)
       if (!map.has(d)) map.set(d, [])
       map.get(d)!.push(s)
     })
     return map
   }, [slots])
   ```
   Салон працює 9:00–19:00 (6:00–16:00 UTC), тому зараз розбіжності немає — але правило зафіксоване, і при зміні годин або таймзони `slice` зламався б тихо.
2. **Крок «Дата»** — `<BookingCalendar mode="booking" availableDates={new Set(byDate.keys())} selected={date} onSelect={setDate} />`.
3. **Крок «Час»** — `byDate.get(date)` замість повного списку. Заголовок: `Вільний час на {дата}`.
4. При зміні послуги або режиму скидати `date` разом зі `slot`.
5. У режимі «будь-який майстер» блок «Хто вільний у цей час» (наявний, `Booking.tsx:143-156`) лишається без змін — він працює від обраного слота.

**Нумерація кроків після зміни:** 1. Послуга → 2. Як записатись → 3. Майстер *(лише в режимі «обрати майстра»)* → 4. Дата *(календар)* → 5. Час → 6. Хто вільний *(лише «будь-який майстер»)* → 7. Підтвердження → 8. Код.

#### 14.3.2. Prefill зі сторінки майстра

```tsx
const location = useLocation()
const prefill = location.state as { masterId?: number; serviceId?: number; startTime?: string } | null

useEffect(() => {
  if (!prefill?.masterId || !prefill.serviceId) return
  setServiceId(prefill.serviceId)
  setMode('master')
  setMasterId(prefill.masterId)
  pendingStart.current = prefill.startTime ?? null
}, [])   // одноразово при монтуванні
```

Нюанси:
- Наявний `useEffect` на `[serviceId, mode, masterId]` перезапитує слоти і **скидає** `setSlot(null)`. Тому `startTime` тримаємо в `pendingStart = useRef<string|null>(null)`, а в `.then(setSlots)` застосовуємо: якщо такий `start` є у списку — `setDate(salonDate(start))` **і** `setSlot(start)`, щоб календар одразу відкрився на потрібній даті; якщо немає — `setErr('Обраний час уже зайнятий, оберіть інший')` і лишити календар відкритим на тій самій даті.
- Банер над кроками: `Запис до майстра {name}` + `<button className="link">Змінити вибір</button>`, що очищає prefill, `masterId`, `mode`, `date`, `slot`.
- `409 Conflict` у `create()` уже обробляється через `catch` → `setErr`. Додати перезапит слотів після 409 і скидання `slot` (дату лишити).

**Не входить:** збереження вибору при редиректі в `/login` (`returnUrl` + `sessionStorage`) — це F-Critical-11/P1-7 у роадмапі. Наслідок для користувача: неавторизований втратить вибір після логіну. Зафіксовано свідомо.

### 14.4. Новий компонент `client/src/components/BookingCalendar.tsx`

Спільний для `/booking` і `/masters/:id` — щоб логіка дат не розійшлась у двох місцях.

**Props:**

```tsx
interface BookingCalendarProps {
  /** Дати (YYYY-MM-DD, у таймзоні салону), на які можна клікати. */
  availableDates: Set<string>
  /** Обрана дата або null. */
  selected: string | null
  onSelect: (date: string) => void
  /** 'schedule' — показуємо робочі дні майстра (до вибору послуги);
   *  'booking'  — показуємо дати з вільними слотами. Впливає лише на title/легенду. */
  mode: 'schedule' | 'booking'
  /** Перший день вікна, YYYY-MM-DD. Дефолт — сьогодні в таймзоні салону. */
  from?: string
  /** Глибина вікна в днях. Дефолт 14 (ScheduleService.WindowDays). */
  windowDays?: number
}
```

**Поведінка:**

1. **Сітка Пн–Нд.** Заголовки — окремий масив `['Пн','Вт','Ср','Чт','Пт','Сб','Нд']`. **Не** використовувати `DAYS` з `api.ts` — там індексація від `0 = Нд` (під `DayOfWeek` бекенда), і для сітки з понеділка вона дасть зсув.
2. **Діапазон** — `from … from + windowDays` (15 дат при дефолтах, R14). Сітка добивається до цілих тижнів → максимум 3 тижневих рядки. **Навігації по місяцях немає** — поза вікном усі дати однаково недоступні. Якщо вікно перетинає межу місяця, у підписі над сіткою показати обидва: «вересень — жовтень 2026».
3. **Стани комірки:**
   | Стан | Вигляд | `disabled` |
   |---|---|---|
   | доступна | звичайна | ні |
   | обрана | `.cal-day--sel` (акцент) | ні |
   | сьогодні | `.cal-day--today` (обведення) | залежить від доступності |
   | у вікні, але недоступна | приглушена, `title` = `mode === 'schedule' ? 'Вихідний' : 'Немає вільного часу'` | так |
   | поза вікном / добивка сітки | майже прозора | так |
4. **Розмітка** — `<button type="button">` на кожну дату (не `<div>`), щоб працювали `Tab` і `Enter`.
5. **A11y:** `aria-label` з повною датою (`Intl.DateTimeFormat('uk-UA', { timeZone: 'Europe/Kyiv', weekday: 'long', day: 'numeric', month: 'long' })`), `aria-pressed={selected === date}`, `aria-current="date"` на сьогодні, `:focus-visible` з контуром. Легенда під сіткою текстом, не лише кольором.
6. **Дата «сьогодні»** — обчислювати як `new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Kyiv' }).format(new Date())`, **не** `new Date().toISOString().slice(0,10)` (той дає UTC-дату).
7. Компонент **не робить запитів** — тільки відображає передані дані.

### 14.5. `styles.css` — доповнення

```css
/* ---------- Сторінка майстра ---------- */
.master-head { display: flex; gap: 20px; align-items: flex-start; margin-bottom: 24px; }
.master-head .master-photo { width: 160px; flex: 0 0 160px; margin-bottom: 0; }
.day--off { opacity: .55; }
.day-hours { color: var(--muted); font-size: 13px; }
.slot--busy { opacity: .4; cursor: not-allowed; text-decoration: line-through; }

/* ---------- Календар вибору дати ---------- */
.calendar { max-width: 360px; }
.calendar__month { font-size: 14px; color: var(--text); margin-bottom: 8px; text-transform: capitalize; }
.calendar__grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 4px; }
.calendar__dow { text-align: center; font-size: 12px; color: var(--muted); padding-bottom: 4px; }
.cal-day {
  aspect-ratio: 1 / 1; display: flex; align-items: center; justify-content: center;
  border: 1px solid var(--glass-30); background: var(--glass-10); color: var(--text);
  border-radius: var(--radius-sm); cursor: pointer; font-family: inherit; font-size: 15px; padding: 0;
  transition: background .2s, border-color .2s;
}
.cal-day:hover:not(:disabled) { background: var(--glass-15); }
.cal-day:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.cal-day:disabled { opacity: .3; cursor: not-allowed; }
.cal-day--sel { background: var(--accent); color: #fff; border-color: var(--accent); }
.cal-day--today { box-shadow: inset 0 0 0 1px var(--accent); }
.cal-day--outside { opacity: .12; border-color: transparent; }
.calendar__legend { margin-top: 10px; font-size: 12px; color: var(--muted); display: flex; gap: 12px; flex-wrap: wrap; }

@media (max-width: 700px) {
  .master-head { flex-direction: column; }
  .master-head .master-photo { width: 100%; flex: none; }
  .calendar { max-width: 100%; }
}
```

### 14.6. Критерій приймання

**Сторінка майстра `/masters/:id`:**

1. `/masters/1` — фото, дані, **календар робочих днів** на 15 дат видно **до** вибору послуги.
2. Вихідні дні майстра в календарі — `disabled` з `title="Вихідний"`; для `TwoTwo` видно чергування 2/2, для `Weekly` — тижневе.
3. Дати поза вікном (16-й день і далі) — `disabled`.
4. Клік на робочу дату до вибору послуги → показані робочі години дня і зайняті інтервали.
5. Вибір послуги → календар переходить у режим слотів: робочі дати без вільного часу стають `disabled` з `title="Немає вільного часу"`.
6. Клік на дату → слоти **тільки цієї дати**, не всі 14 днів.
7. Вибір слота → «Забронювати» → `/booking` одразу на кроці «Підтвердження», з правильними майстром/послугою/часом.
8. `/masters/999`, `/masters/abc`, неактивний майстер → «Майстра не знайдено», без білого екрана.

**Сторінка `/booking` (прямий вхід):**

9. Вибір послуги → «Будь-який майстер» → **календар** з датами, де є хоч один вільний слот; дати без слотів `disabled`.
10. Клік на дату → слоти лише цієї дати; блок «Хто вільний у цей час» працює як раніше.
11. Режим «обрати майстра» → після вибору майстра календар показує лише його доступні дати.
12. Зміна послуги скидає і дату, і слот.
13. Перехід зі сторінки майстра → календар одразу відкритий на даті прибулого слота, слот виділений.
14. Якщо прибулий слот уже зайняли → повідомлення «Обраний час уже зайнятий, оберіть інший», календар лишається на тій самій даті.
15. `409` при бронюванні → слоти перезапитані, дата збережена, слот скинутий.

**Спільне:**

16. Неавторизований на `/booking` → редирект на `/login` (втрата вибору — відомий і задокументований наслідок).
17. Час збігається між календарем, слотами, `/booking` і SMS (перевірка `SalonClock`): слот `09:00` у салоні = `06:00Z` або `07:00Z залежно від DST.
18. **Межа доби:** о 23:30 за Києвом «сьогодні» в календарі — правильна дата (перевірка, що використано `sv-SE` + `timeZone`, а не `toISOString().slice(0,10)`).
19. Навігація з клавіатури: `Tab` до сітки, `Enter` обирає дату, фокус видно; `aria-pressed` на обраній даті.
20. 320px — сітка календаря вміщується без горизонтального скролу; шапка майстра в стовпчик.

**Оцінка:** 4 год (2,5 год сторінка + 1,5 год календар і перебудова кроків у `Booking.tsx`).


---

## 15. Фаза 9 — Адмінка: керування фото

### 15.1. Передумова — кодування `Admin.tsx` (D6)

`client/src/pages/Admin.tsx` починається з BOM (`\uFEFF`), а `docs/design/PLAN.md` і `03-infra-qa.md` §3.4 фіксують мохібейк кирилиці в цьому файлі.

**Перед першою правкою:** відкрити файл і переконатися, що українські рядки читаються («Адмін-панель», «Новий майстер», «Робочі години»). Якщо ні — спершу перекодувати у UTF-8 і перевірити рендер, і **лише потім** додавати функціонал. Інакше кожна правка множить пошкоджені рядки.

### 15.2. Зміни у формі майстра

`emptyMaster()` — додати `photoUrl: ''`.

У картці форми, після поля «Спеціальність», додати три відсутні контролі. Два з них — виправлення D5, без якого керування персоналом (Фаза 11) неможливе з UI:

1. **`bio`** (відсутнє зараз):
   ```tsx
   <input placeholder="Про майстра" value={mstForm.bio}
          onChange={e => setMstForm({ ...mstForm, bio: e.target.value })} />
   ```
2. **`isActive`** (відсутнє зараз) — саме цей чекбокс дозволяє повернути звільненого майстра:
   ```tsx
   <label>
     <input type="checkbox" checked={mstForm.isActive}
            onChange={e => setMstForm({ ...mstForm, isActive: e.target.checked })} />
     {' '}Активний (працює в салоні)
   </label>
   ```
   **Увага на CSS:** глобальне правило `input, select { display: block; width: 100%; }` розтягне чекбокс. Додати в `styles.css`:
   ```css
   input[type="checkbox"] { display: inline-block; width: auto; margin: 0 6px 0 0; vertical-align: middle; }
   ```
3. **Фото** — блок, активний лише для збереженого майстра:
   ```tsx
   {mstForm.id > 0 ? (
     <div className="photo-edit">
       <label>Фото майстра</label>
       {mstForm.photoUrl
         ? <img className="photo-preview" src={resolvePhotoUrl(mstForm.photoUrl)!} alt="Поточне фото" />
         : <p className="muted">Фото не завантажено</p>}
       <input type="file" accept="image/jpeg,image/png,image/webp" onChange={onPickPhoto} />
       {mstForm.photoUrl && <button className="link" onClick={onRemovePhoto}>Прибрати фото</button>}
     </div>
   ) : (
     <p className="muted">Фото можна завантажити після збереження майстра.</p>
   )}
   ```

### 15.3. Обробники

```tsx
const onPickPhoto = async (e: React.ChangeEvent<HTMLInputElement>) => {
  const file = e.target.files?.[0]
  if (!file) return
  setErr('')
  // клієнтська перевірка — щоб не ганяти 3 МБ по мережі заради 400
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    setErr('Дозволені лише файли JPG, PNG або WebP'); return
  }
  if (file.size > 3 * 1024 * 1024) { setErr('Розмір файлу перевищує 3 МБ'); return }
  try {
    const r = await uploadMasterPhoto(mstForm.id, file)
    setMstForm({ ...mstForm, photoUrl: r.photoUrl })
    load()
  } catch (ex) { setErr(errMsg(ex)) }
  finally { e.target.value = '' }   // щоб повторний вибір того ж файлу тригерив onChange
}

const onRemovePhoto = async () => {
  if (!confirm('Прибрати фото майстра?')) return
  try {
    await deleteMasterPhoto(mstForm.id)
    setMstForm({ ...mstForm, photoUrl: '' })
    load()
  } catch (ex) { setErr(errMsg(ex)) }
}
```

Клієнтська перевірка **дублює** серверну і не замінює її — сервер лишається джерелом істини (magic bytes, розширення, розмір).

### 15.4. Кнопка «Ред.» у таблиці майстрів

У наявному `setMstForm({ ...emptyMaster(), id: m.id, ... })` додати `photoUrl: m.photoUrl ?? ''`.

### 15.5. Тіло `saveMaster`

Наявний `body` розпилює `...mstForm`, тому `photoUrl` поїде автоматично. З рішенням 8.4 (`if (dto.PhotoUrl != null)`) це безпечно: порожній рядок з форми фото не обнулить, бо обнулення — тільки через `DELETE`.
**Перевірити:** що `photoUrl: ''` не перетирає збережений шлях. Якщо перетирає — не надсилати поле взагалі: `const { photoUrl: _, ...rest } = mstForm`.

### 15.6. Стилі

```css
.photo-edit { margin: 12px 0; }
.photo-preview {
  display: block; width: 120px; height: 120px; object-fit: cover;
  border-radius: var(--radius); border: 1px solid var(--glass-15); margin: 8px 0;
}
input[type="file"] { padding: 8px; }
```

### 15.7. Критерій приймання

1. Створення нового майстра → блок фото показує підказку, інпута файлу немає.
2. Після збереження і повторного «Ред.» → інпут файлу доступний.
3. Upload → прев'ю оновилось, у таблиці й на головній фото з'явилось.
4. Файл 5 МБ → помилка **до** відправки (перевірити в Network, що запиту не було).
5. `.exe` → браузер не дає обрати (через `accept`); при обході — помилка з сервера.
6. «Прибрати фото» → прев'ю зникло, на головній fallback.
7. Редагування `bio` і `isActive` зберігається (перевірити `GET /api/admin/masters`).
8. Чекбокс `isActive` не розтягнутий на всю ширину.
9. Українські рядки у файлі не пошкоджені після правки.

**Оцінка:** 1,5 год.

---

## 16. Фаза 10 — Звільнення майстра

**Залежність:** Фаза 4 (`ScheduleService.ValidateAsync`), Фаза 9 (чекбокс `isActive` для повернення).
**Перетин з роадмапом:** частково закриває **C3** (валідація при зміні запису) для нового ендпоінта. Повний **C4** (матриця переходів статусів + `AuditLog`) лишається в роадмапі — див. 16.6.

### 16.1. Що вже працює (не переробляти)

`AdminController.DeleteMaster`: якщо в майстра є **будь-які** записи → soft-delete (`IsActive = false`), інакше hard-delete з чисткою `WorkingHours` і `MasterServices`. Далі:

- `CatalogController.Masters()` фільтрує `IsActive` → майстер зникає з головної;
- `ScheduleService` фільтрує `IsActive` → слоти не генеруються;
- `BookingController.Create` вимагає `IsActive` → нове бронювання неможливе;
- історія записів не ламається (`AccountController` і `AdminController` віддають `a.Master.Name`);
- `WorkingHours`/`MasterServices` зберігаються → майстра можна повернути.

### 16.2. Дірки, які закриває ця фаза

1. **Майбутні `Confirmed`-записи залишаються живими** — клієнт прийде до майстра, якого немає.
2. **`ReminderWorker` надішле нагадування** по таких записах: його фільтр (`ReminderWorker.cs:36-39`) — `Status == Confirmed && ReminderSentAt == null && StartTime` у вікні ±10 хв від «через 3 години». Перевірки `Master.IsActive` **немає**.
3. **Немає способу переназначити запис іншому майстру:** `reschedule` змінює лише `StartTime`, `status` — лише статус. Змінити `MasterId` через API неможливо.

### 16.3. `GET /api/admin/masters/{id}/deactivation-impact`

**200:**

```json
{
  "masterId": 1,
  "masterName": "Оксана",
  "futureAppointments": [
    {
      "id": 42,
      "startTime": "2026-09-05T07:00:00Z",
      "endTime": "2026-09-05T07:30:00Z",
      "serviceId": 1,
      "service": "Стрижка чоловіча класична",
      "client": "Марія",
      "phone": "+380671112233",
      "status": "Confirmed",
      "candidates": [ { "id": 2, "name": "Ірина" }, { "id": 3, "name": "Наталія" } ]
    }
  ]
}
```

- Вибірка: `MasterId == id`, `StartTime > DateTime.UtcNow`, `Status ∈ { Confirmed, PendingVerification }`, сортування за `StartTime`.
- `candidates` — активні майстри, які: мають цю послугу через `MasterServices`, працюють цього дня, і в яких цей час вільний → перевірка через `ScheduleService.ValidateAsync(candidateId, serviceId, startUtc)`.
- Дані клієнта тут доречні: ендпоінт адмінський (`[Authorize(Roles="Admin")]` з класу).
- 404: `{ "error": "Майстра не знайдено" }`.

### 16.4. `PUT /api/admin/appointments/{id}/master`

**Запит:** `{ "masterId": 2 }`

| Код | Тіло | Умова |
|---|---|---|
| 200 | `{ "ok": true }` | успіх |
| 400 | `{ "error": "<текст із ValidateAsync>" }` | новий майстер не працює цього дня / час поза робочими годинами / не надає цю послугу / поза вікном |
| 404 | `{ "error": "Запис не знайдено" }` / `{ "error": "Майстра не знайдено" }` | — |
| 409 | `{ "error": "Цей час у нового майстра вже зайнятий" }` | накладка |

Реалізація:
1. Завантажити запис із `Service`.
2. `schedule.ValidateAsync(dto.MasterId, appt.ServiceId, appt.StartTime, ignoreAppointmentId: appt.Id)` — параметр `ignoreAppointmentId` потрібен, щоб запис не конфліктував сам із собою.
3. `appt.MasterId = dto.MasterId; appt.ReminderSentAt = null;` — щоб нагадування пішло з новим іменем майстра.
4. Нотифікація клієнту: `«Ваш запис {послуга} {дата} перенесено до майстра {новий майстер}.»` через `notifier.SendAsync(appt.NotifyChannel, recipient, msg)`.
5. **`Status` не змінювати** (наявний `Reschedule` самовільно піднімає `Pending → Confirmed` — це дефект C3, не повторювати).

**Транзакція:** обгорнути в `Serializable`, як у `BookingController.Create`, щоб не з'явилась накладка між перевіркою і записом.

**Ризик нотифікації в транзакції:** `NotificationService.SendAsync` робить власний `SaveChangesAsync` (запис у `NotificationLogs`) — §1.4 рев'ю попереджає про неконсистентність при виклику в зовнішній транзакції. **Рішення для цієї фази:** нотифікації надсилати **після** `tx.CommitAsync()`, поза транзакцією. Outbox — задача P2.

### 16.5. `POST /api/admin/masters/{id}/deactivate`

**Запит:**

```json
{
  "policy": "CancelAll",
  "reassignments": []
}
```

`policy` ∈ `CancelAll` | `Reassign` | `KeepAppointments` (обов'язковий — явний вибір, без тихого дефолту).

| policy | Дія |
|---|---|
| `CancelAll` | усі майбутні `Confirmed`/`Pending` → `Cancelled` + нотифікація кожному клієнту |
| `Reassign` | застосувати всі пари з `reassignments` через логіку 16.4; **якщо покрито не всі майбутні записи → 400** `{ "error": "Не для всіх майбутніх записів обрано нового майстра" }` |
| `KeepAppointments` | нічого не робити із записами (усвідомлений вибір адміна) |

Далі в усіх випадках: `m.IsActive = false`, `SaveChangesAsync`.
`WorkingHours` і `MasterServices` **не видаляти** — потрібні для повернення.
Файл фото **не видаляти** (9.5).

**200:** `{ "ok": true, "cancelled": 3, "reassigned": 0, "kept": 0 }`

Текст нотифікації скасування: `«Ваш запис {послуга} {дата} скасовано: майстер {ім'я} більше не працює. Просимо записатись повторно — вибачте за незручності.»`

### 16.6. Узгодження з наявним `DELETE`

`DELETE /api/admin/masters/{id}` лишається **лише для майстрів без записів** (hard-delete + видалення фото). Гілку soft-delete з нього прибрати і повертати 409:

```json
{ "error": "У майстра є записи. Використайте деактивацію з обробкою майбутніх записів." }
```

Це прибирає два різні шляхи деактивації з різною поведінкою.

**`Admin.tsx`:** кнопку «Видалити» замінити на «Звільнити», яка відкриває блок з `deactivation-impact`: список майбутніх записів, для кожного — select з `candidates`, і три кнопки політики. Для майстра без записів — одразу `DELETE` з підтвердженням.

### 16.7. `ReminderWorker` — фільтр активності

`server/Services/ReminderWorker.cs`, у `Where`:

```csharp
.Where(a => a.Status == AppointmentStatus.Confirmed
         && a.Master.IsActive          // ← додати
         && a.ReminderSentAt == null
         && a.StartTime >= from && a.StartTime <= to)
```

Найдешевша правка фази — один рядок, а закриває розсилку нагадувань «до майстра, якого немає». Працює разом з `Include(a => a.Master)`, який уже є.

### 16.8. Хелпер отримувача нотифікації

Логіка «`Email` → `Client.Email ?? Phone`, інакше `Client.ExternalContact ?? Phone`» зараз живе в `ReminderWorker.cs:44` і знадобиться у 16.4 та 16.5. Винести в `NotificationService` як статичний метод:

```csharp
public static string ResolveRecipient(User client, NotifyChannel channel) =>
    channel == NotifyChannel.Email ? (client.Email ?? client.Phone)
                                   : (client.ExternalContact ?? client.Phone);
```

і використати в усіх трьох місцях.

### 16.9. Повернення майстра на роботу

Окремий ендпоінт **не потрібен**: `UpdateMaster` уже приймає `IsActive`, а чекбокс з'явився у Фазі 9. Додати в `Admin.tsx` візуальне розділення таблиці майстрів на «Активні» та «Неактивні» (просто два блоки за `m.isActive`), щоб звільнених було видно.

### 16.10. Що НЕ входить (лишається в роадмапі)

- **`AuditLog {who, what, when}`** (C4/P0-5) — масове скасування без аудиту небажане, але таблиця і матриця переходів статусів — окрема задача. **Мітигація на цю фазу:** усі дії пишуться через `ILogger` з `masterId`, `appointmentId`, `policy` і користувачем з JWT.
- Матриця дозволених переходів статусів (C4).
- Пагінація `admin/appointments` (M9/P1-6) — `deactivation-impact` віддає лише майбутні записи одного майстра, тому OOM тут не загрожує.

### 16.11. Критерій приймання

1. `deactivation-impact` для майстра з майбутніми записами повертає їх усі, з непорожніми `candidates` там, де є вільні майстри.
2. `CancelAll` → усі майбутні записи `Cancelled`, у `NotificationLogs` є повідомлення на кожного клієнта, майстер `IsActive = false`.
3. `Reassign` з повним покриттям → записи в нових майстрів, `ReminderSentAt = null`, старі слоти звільнились (перевірити `slots`).
4. `Reassign` з неповним покриттям → 400, **нічого не змінилось** (перевірити, що майстер лишився активним).
5. Переназначення на майстра, який не надає послугу → 400.
6. Переназначення на зайнятий час → 409.
7. Після деактивації: майстер зник з головної, `/masters/{id}` → 404, у слотах відсутній.
8. Нагадування по записах деактивованого майстра **не надсилаються** (створити запис за 3 год, деактивувати, дочекатись циклу воркера, перевірити `NotificationLogs`).
9. Чекбокс `isActive` → `true` повертає майстра: з'явився на головній, слоти генеруються.
10. `DELETE` для майстра з записами → 409 з підказкою.
11. `DELETE` для майстра без записів → видалений разом із файлом фото.

**Оцінка:** 3,5–4 год.


---

## 17. Фаза 11 — Найм: захист від помилок конфігурації

### 17.1. Проблема

`RotationAnchor` задається в адмінці звичайним `<input type="date">`. Семантика неочевидна:
- `Weekly` → анкор мусить бути **понеділком робочого тижня**;
- `TwoTwo` → **першим робочим днем зміни**.

Підпис у формі вже є (`Admin.tsx`: «(понеділок робочого тижня)» / «(перший робочий день зміни 2/2)»), але помилка на один день ставить майстра в протилежну зміну, і адмін дізнається про це лише побачивши порожній або несподіваний календар. Прев'ю знімає проблему повністю.

### 17.2. `POST /api/admin/rotation-preview`

Ендпоінт (а не розрахунок на TypeScript) — щоб правило ротації **не дублювалось у двох мовах** і не розсинхронізувалось при зміні `RotationHelper`.

**Запит:** `{ "rotationType": "TwoTwo", "rotationAnchor": "2026-09-03" }`

**200:**

```json
[
  { "date": "2026-09-03", "isWorking": true },
  { "date": "2026-09-04", "isWorking": true },
  { "date": "2026-09-05", "isWorking": false }
]
```

**400:** `{ "error": "Невідомий тип графіка" }` — якщо `rotationType` не парситься.

Реалізація: `schedule.PreviewRotation(type, anchor)` — 15 днів від `clock.TodayInSalon`, **без звернення до БД** (працює для ще не збереженого майстра).

C#:

```csharp
public record RotationPreviewDto(string RotationType, DateOnly RotationAnchor);
```

### 17.3. Прев'ю у формі майстра

`Admin.tsx`, під полем `rotationAnchor`:

- `useEffect` на `[mstForm.rotationType, mstForm.rotationAnchor]` → `previewRotation(...)` (з debounce ~300 мс, бо `type="date"` тригерить onChange на кожну зміну);
- рендер 15 бейджів: дата + `✅`/`🏖`, у стилі `.slots`/`.slot`;
- підпис: «Так виглядатиме графік цього майстра на 15 днів».

### 17.4. Валідація повноти даних майстра

Зараз API дозволяє створити майстра **без робочих годин** або **без жодної послуги**. Такий майстер ніколи не з'явиться у слотах (`wh == null → continue`; фільтр `MasterServices.Any(...)`) — тихий фейл, який виглядає як баг системи.

**Сервер** (`AddMaster`/`UpdateMaster`) — 400 з явними текстами:
- `{ "error": "Оберіть хоча б одну послугу майстра" }` — якщо `ServiceIds` порожній;
- `{ "error": "Задайте робочі години хоча б для одного дня" }` — якщо `WorkingHours` порожній;
- `{ "error": "Час закінчення має бути пізніше часу початку" }` — якщо для будь-якого дня `End <= Start`;
- `{ "error": "Для одного дня тижня задано кілька інтервалів" }` — якщо в `WorkingHours` дубль `DayOfWeek` (наявний код додає всі рядки, а `Slots` бере `FirstOrDefault` → другий інтервал молча ігнорується).

Це вузький підмножинний фікс **M8** (повна валідація всіх DTO через FluentValidation лишається задачею роадмапу).

**Клієнт** — не давати натиснути «Додати майстра» з тими самими умовами (`disabled` + підказка), щоб не гнати запит на очевидну 400.

### 17.5. Кнопка швидкого заповнення годин

У формі: `+ Робочі години як у салону (9:00–19:00, щодня)` — заповнює 7 днів, як у `DbInitializer`. Наявний `emptyMaster()` дає лише Пн–Пт, хоч салон працює щодня — це розходження з сідом, яке плутає.

### 17.6. Критерій приймання

1. Зміна `rotationType` або `rotationAnchor` → прев'ю оновлюється; для `TwoTwo` видно чергування 2/2, для `Weekly` — тижневе.
2. Прев'ю збігається з реальними слотами після збереження (перевірити 3 дні вибірково).
3. Прев'ю коректне для якоря **в минулому** і **в майбутньому** (регресія D2).
4. Спроба зберегти майстра без послуг → кнопка `disabled`; при обході (curl) → 400.
5. Те саме без робочих годин.
6. `End <= Start` → 400.
7. Дубль дня тижня → 400.
8. Кнопка швидкого заповнення дає 7 днів 09:00–19:00.

**Оцінка:** 2 год.

---

## 18. Фаза 12 — Наскрізна перевірка

### 18.1. Автоматизовані команди

```powershell
dotnet build server                    # без нових warning'ів
dotnet ef migrations list --project server   # InitialCreate + AddMasterPhotoUrl застосовані
cd client; npm run build               # tsc -b && vite build без помилок
```

### 18.2. Повторити smoke-тест Фази 0

Пункти 6.3.1–6.3.8 — після завершення всіх фаз, щоб переконатись, що функціонал не зламав базові сценарії.

### 18.3. Наскрізний сценарій «клієнт»

1. Головна → бачу 6 майстрів, у 2+ є фото, у решти — ініціали.
2. Клік по майстру з фото → `/masters/:id`, бачу **календар робочих днів** на 15 дат; вихідні недоступні.
3. Обираю послугу → недоступні стають і робочі дати без вільного часу.
4. Клік на дату → слоти **лише цієї дати**.
5. Обираю слот → «Забронювати» → `/booking` на кроці підтвердження.
6. Не авторизований → `/login`, вхід за кодом (`devCode` з відповіді).
7. Повертаюсь на `/masters/:id`, повторюю вибір → бронювання → код → `/cabinet`.
8. У кабінеті запис видно з правильним майстром, послугою і часом.
9. Скасовую запис → дата й слот знову доступні на `/masters/:id`.
10. Прямий вхід на `/booking`: послуга → «будь-який майстер» → **календар** → дата → слоти всіх майстрів → вибір майстра зі слота → бронювання.

### 18.4. Наскрізний сценарій «адмін»

1. Вхід адміном.
2. Створюю майстра: ім'я, спеціальність, bio, `TwoTwo`, якір = сьогодні, послуги, години «як у салону». Прев'ю показує 2/2.
3. Зберігаю → завантажую фото → бачу прев'ю.
4. Головна → новий майстер з фото; `/masters/:id` працює.
5. Клієнт бронює до нього час (окрема вкладка).
6. «Звільнити» → `deactivation-impact` показує цей запис і кандидатів.
7. `Reassign` на іншого майстра → запис перейшов, майстер деактивований.
8. Клієнт у кабінеті бачить нового майстра; у `NotificationLogs` є повідомлення.
9. Ставлю `isActive = true` → майстер знову на головній.

### 18.5. Матриця перевірки безпеки

| Перевірка | Очікуване |
|---|---|
| `POST /api/admin/masters/1/photo` без токена | 401 |
| Те саме з токеном клієнта (не адміна) | 403 |
| Файл 5 МБ | 400, файл не збережено |
| `.txt` → `.jpg` (magic bytes) | 400 |
| `filename="../../appsettings.json"` | 200, файл під згенерованим ім'ям у цільовій папці; поза папкою нічого не створено |
| `GET /uploads/masters/../../appsettings.json` | 404 (`UseStaticFiles` не виходить за `wwwroot`) |
| `GET /api/catalog/masters/1/schedule` | 200, у JSON немає `client`, `phone`, `service` |
| `GET /api/admin/masters/1/deactivation-impact` без токена | 401 |

### 18.6. A11y та адаптив

- 320 / 375 / 768 / 1440 px — картки, шапка майстра, слоти, таблиці адмінки.
- `Tab`-навігація: картка майстра → фокус видно; слоти → фокус видно; чекбокс `isActive` доступний.
- `alt` у всіх `<img>`; декоративний fallback — `aria-hidden`.
- Контраст: підписи в картці й на сторінці майстра — `--text`/`--muted`, не `--text-dim` на фото (D12).

### 18.7. Автотести

Тестового проєкту немає, покриття ≈ 0 (`03-infra-qa.md` §3.3). **Автотести в цей план не входять** — додаються лише за окремим запитом. Якщо додавати, першими кандидатами (за роадмапом P2) є:

- xUnit: `ScheduleService.GetSlotsAsync` (ротація `Weekly`/`TwoTwo`, межа доби, `Pending` 15 хв), `ValidateAsync` (накладки), паралельне подвійне бронювання.
- vitest: групування слотів по днях, `resolvePhotoUrl`, fallback у `MasterPhoto`.

**Оцінка:** 1,5–2 год.

---

## 19. Таблиця файлів

### 19.1. Бекенд

| Файл | Дія | Що саме | Фаза |
|---|---|---|---|
| `server/Beauty.Server.csproj` | змінити | TFM `net10.0`; 4 пакети на нові версії | 0 |
| `server/Models/Entities.cs` | змінити | `Master.PhotoUrl` | 2 |
| `server/Migrations/*` | створити | `InitialCreate`, `AddMasterPhotoUrl`, snapshot | 1, 2 |
| `server/Data/AppDbContext.cs` | змінити | прибрати `Seed` (переїзд у `DbInitializer`) | 1 |
| `server/Data/DbInitializer.cs` | створити | `Migrate()` + перенесений ідемпотентний сід | 1 |
| `server/Data/RotationHelper.cs` | змінити | нормалізація модуля у `Weekly` (D2) | 4 |
| `server/Services/ScheduleService.cs` | створити | `GetSlotsAsync`, `GetMasterScheduleAsync`, `PreviewRotation`, `ValidateAsync`, `WindowDays` | 4 |
| `server/Models/ScheduleDtos.cs` | створити | `SlotDto`, `SlotMasterDto`, `BusyIntervalDto`, `MasterDayDto`, `BookingCheck` | 4 |
| `server/Services/NotificationService.cs` | змінити | статичний `ResolveRecipient` | 10 |
| `server/Services/ReminderWorker.cs` | змінити | фільтр `a.Master.IsActive`; використати `ResolveRecipient` | 10 |
| `server/Controllers/BookingController.cs` | змінити | `Slots` і `Create` через `ScheduleService`; прибрати `BookingWindowDays`; `CancellationToken` | 4 |
| `server/Controllers/CatalogController.cs` | змінити | `photoUrl` у списку; `clock.TodayInSalon` (D1); `GET masters/{id}`; `GET masters/{id}/schedule`; явні `[AllowAnonymous]` | 2, 4, 5 |
| `server/Controllers/AdminController.cs` | змінити | `PhotoUrl` у `MasterDto` і проєкції; `POST/DELETE .../photo`; `deactivation-impact`; `deactivate`; `PUT appointments/{id}/master`; `rotation-preview`; валідація повноти; `DELETE` → 409 при записах | 2, 3, 10, 11 |
| `server/Program.cs` | змінити | `UseStaticFiles()`; `AddScoped<ScheduleService>()`; `DbInitializer.Seed` | 1, 3, 4 |
| `server/appsettings.json` | змінити | блок `Uploads` | 3 |
| `server/wwwroot/uploads/masters/` | створити (якщо відсутня) | — | 3 |

### 19.2. Клієнт

| Файл | Дія | Що саме | Фаза |
|---|---|---|---|
| `client/src/types.ts` | змінити | `MasterDto.photoUrl` + опційність; 5 нових інтерфейсів; `MasterForm.photoUrl` | 6 |
| `client/src/api.ts` | змінити | `fetchMaster`, `fetchMasterSchedule`, `resolvePhotoUrl`, `uploadMasterPhoto`, `deleteMasterPhoto`, `previewRotation` | 6 |
| `client/src/components/MasterPhoto.tsx` | створити | фото + fallback з ініціалами + `onError` | 7 |
| `client/src/components/MasterCard.tsx` | створити | клікабельна картка на `<Link>` | 7 |
| `client/src/components/BookingCalendar.tsx` | створити | сітка Пн–Нд на вікно 14 днів, режими `schedule`/`booking`, a11y | 8 |
| `client/src/pages/Home.tsx` | змінити | `<MasterCard>`; `loading`/`catch` (D7) | 7 |
| `client/src/pages/MasterSchedule.tsx` | створити | сторінка `/masters/:id`: календар робочих днів → послуга → дата → слоти | 8 |
| `client/src/App.tsx` | змінити | роут `/masters/:id` | 8 |
| `client/src/pages/Booking.tsx` | змінити | `BookingCalendar` замість списку всіх днів; групування по **салонній** даті; prefill з `location.state` через `pendingStart`; банер; перезапит після 409; прибрати `services?.` | 8 |
| `client/src/pages/Admin.tsx` | змінити | перевірити кодування (D6); поля `bio`/`isActive` (D5); блок фото; прев'ю ротації; валідація; розділення активні/неактивні; «Звільнити» замість «Видалити» | 9, 10, 11 |
| `client/src/styles.css` | змінити | тільки доповнення: картка, фото, сторінка майстра, **календар**, чекбокс, прев'ю | 7, 8, 9 |
| `client/vite.config.ts` | змінити | проксі `/uploads` (страховка) | 3 |

### 19.3. Інфраструктура та документація

| Файл | Дія | Що саме | Фаза |
|---|---|---|---|
| `README.md` | змінити | .NET 10; вимога SDK; розділ «Бекап» (двокроковий) | 0, 3 |
| `docs/review/04-roadmap.md` | змінити | P2: `sdk:9.0` → `10.0`; позначити P1-1 і P1-2 як виконані цим планом | 0, 1, 4 |

**Не створюємо:** `global.json` (R11), `Dockerfile` (R7), тестовий проєкт (18.7).

---

## 20. Порядок і залежності

```
0 (.NET 10)
  └─> 1 (baseline міграції)
        └─> 2 (PhotoUrl)
              └─> 3 (upload)
                    └─> 4 (ScheduleService + D1/D2/D3/D4)   ← вузол, від якого залежать 5, 10, 11
                          ├─> 5 (API сторінки майстра)
                          │     └─> 6 (типи/api) ─> 7 (картка) ─> 8 (сторінка + Booking)
                          ├─> 9 (адмінка: фото, bio, isActive)
                          │     └─> 10 (звільнення)
                          └─> 11 (найм: прев'ю, валідація)
                                └─> 12 (наскрізна перевірка)
```

**Жорсткі залежності:**

- **0 → 1:** baseline має генеруватись EF 10, інакше snapshot доведеться регенерувати, а `ProductVersion` розійдеться з рантаймом.
- **1 → 2:** без baseline міграція `AddMasterPhotoUrl` не застосується до наявної БД.
- **2 → 3:** upload пише в колонку, якої без Фази 2 немає.
- **4 → 5, 10, 11:** усі три спираються на `ScheduleService` (`GetMasterScheduleAsync`, `ValidateAsync`, `PreviewRotation`).
- **9 → 10:** без чекбокса `isActive` звільненого майстра неможливо повернути з UI.
- **3 → 9:** адмінка вантажить фото через ендпоінт із Фази 3.

**Мінімальний зріз під початковий запит користувача** (фото + сторінка графіка): фази **0, 1, 2, 3, 4, 5, 6, 7, 8** ≈ 12–13 год.
Фази 9–11 (адмінка + персонал) можна виконати другою ітерацією, **але пункт 16.7** (`ReminderWorker` + `Master.IsActive`) варто взяти одразу — це один рядок, а без нього система шле нагадування до звільнених майстрів.

Бек-лог виправлень за блоками A–C: `docs/review/Kiro/fixes-after-blocks-ABC.md`.

---

## 21. Оцінка

| Фаза | Назва | Оцінка |
|---|---|---|
| 0 | Перехід на .NET 10 + пакети + smoke-тест | 2–3 год |
| 1 | EF Migrations через baseline | 1,5–2 год |
| 2 | Поле `PhotoUrl` | 0,5 год |
| 3 | Upload: конфіг, статика, ендпоінти, безпека | 2,5 год |
| 4 | `ScheduleService` + виправлення D1–D4 + регресія | 2 год |
| 5 | API сторінки майстра | 1 год |
| 6 | Клієнт: типи та API | 0,7 год |
| 7 | `MasterCard` + `Home.tsx` | 1,5 год |
| 8 | Сторінка `/masters/:id` + `BookingCalendar` + `Booking.tsx` | 4 год |
| 9 | Адмінка: фото, `bio`, `isActive` | 1,5 год |
| 10 | Звільнення майстра | 3,5–4 год |
| 11 | Найм: прев'ю ротації, валідація | 2 год |
| 12 | Наскрізна перевірка | 1,5–2 год |
| | **Разом** | **24,2–27,2 год** |

Оцінки — чистий час роботи без урахування простоїв на з'ясування невідомих із розділу 22.

---

## 22. Ризики

| # | Ризик | Ймовірність | Вплив | Мітигація |
|---|---|---|---|---|
| Р1 | `InitialCreate` (EF 10) не збігається зі схемою від `EnsureCreated()` (EF 9) | середня | висока: baseline треба правити вручну | Чек-лист 7.3 по 8 таблицях; джерело істини — фактична БД; дамп зроблено (7.1) |
| Р2 | `BCrypt.Net-Next` 4.2.1 не верифікує хеш від 4.0.3 | низька | висока: втрата доступу адміна | Блокуючий тест 6.3.3; відкат пакета на 4.0.3 без відкату решти |
| Р3 | Зміна claim-mapping у JWT після .NET 10 → `NullReferenceException` на `FindFirst(MobilePhone)!` | низька | висока: бронювання не працює | Smoke-тест 6.3.5, 6.3.7; при потребі — прибрати `!` і дати явну 401 |
| Р4 | Рефакторинг слотів змінює форму відповіді → `Booking.tsx` ламається | середня | середня | Порівняння JSON до/після (10.6.1–10.6.2) як окремий крок приймання |
| Р5 | `UpdateMaster` обнуляє `PhotoUrl` при збереженні форми | висока, якщо не врахувати | середня | Рішення 8.4: `if (dto.PhotoUrl != null)`; обнулення лише через `DELETE` |
| Р6 | Фото не завантажується в dev через шлях/проксі | середня | низька | `resolvePhotoUrl` через `VITE_API_URL` (9.6) + проксі `/uploads` як страховка |
| Р7 | Правка `Admin.tsx` множить пошкоджену кирилицю (D6) | середня | середня | Перевірка кодування **до** першої правки (15.1) |
| Р8 | Масове скасування записів без аудиту — неможливо з'ясувати, хто і що скасував | висока | середня | Логування через `ILogger` з `masterId`/`policy`/користувачем (16.10); `AuditLog` — задача роадмапу |
| Р9 | Нотифікації в транзакції деактивації дають неконсистентність | середня | середня | Надсилати після `Commit` (16.4) |
| Р10 | `csharp-ls` не підтримує `net10.0` → зникає діагностика | невідома | низька | Не блокує роботу; вирішується окремо (6.5) |
| Р11 | Втрата папки `uploads` при бекапі лише через `pg_dump` | середня | середня | Двокрокова процедура в `README.md` (9.7); fallback на ініціали не дає UI зламатись |

---

## 23. Свідомо не зроблене (щоб не шукати вдруге)

- `global.json` — не додаємо (R11); вимога SDK фіксується текстом у `README.md`.
- Контейнеризація сервера, `Dockerfile`, том для `uploads` у compose — D2 відкладено (R7).
- Збереження вибору при редиректі в `/login` (`returnUrl` + `sessionStorage`) — F-Critical-11/P1-7. **Наслідок:** неавторизований користувач втрачає обраний слот після входу.
- `AuditLog` і матриця переходів статусів — C4/P0-5.
- Повна валідація DTO (FluentValidation) — M8; зроблено лише вузьке підмножинне для майстра (17.4).
- Пагінація `admin/appointments` — M9/P1-6.
- Фіксований `RotationAnchor` у сіді — M11 (змінює дані).
- Виправлення `(DayOfWeek)d` і `DateTime.UtcNow` у сіді — D8 (перенесено «як є» у Фазі 1).
- `AbortController` у всіх запитах — F-Major-13; зроблено лише прапорець `alive` у `Home.tsx` і `MasterSchedule.tsx`.
- Навігація по місяцях у календарі та бронювання далі ніж на 14 днів — вікно обмежене `ScheduleService.WindowDays` (R14). Розширення вікна = зміна бізнес-правила, окреме рішення.
- Показ у календарі кількості вільних слотів на дату (бейдж «×5») — не робимо; дата лише доступна/недоступна.
- Підключення `design/tokens.css` — дрейф дизайн/код (`03-infra-qa.md` §3.5); нові стилі пишуться на змінних із `styles.css`.
- `UseExceptionHandler`, security headers, HSTS — M12.
- Автотести — 18.7.
- Кореневий `.env` для `docker compose` (його немає) і `dead code` `adminPhone` у `AuthController.cs:89` — дрібні передумови поза планом.

---

## 24. Джерела

**Прочитаний код:** `server/`: `Program.cs`, `appsettings.json`, `Beauty.Server.csproj`, `Models/Entities.cs`, `Data/AppDbContext.cs`, `Data/RotationHelper.cs`, `Controllers/{Auth,Account,Admin,Booking,Catalog}Controller.cs`, `Services/{JwtService,SalonClock,NotificationService,ReminderWorker}.cs`.
`client/`: `App.tsx`, `api.ts`, `auth.tsx`, `types.ts`, `styles.css`, `vite.config.ts`, `package.json`, `.env`, `.env.example`, `pages/{Home,Booking,Login,Cabinet,Admin}.tsx`.
Корінь: `README.md`, `docker-compose.yml`.

**Рев'ю та дизайн:** `docs/review/{00-README,01-backend,02-frontend,03-infra-qa,04-roadmap}.md`, `docs/review/Kiro/quick-wins-check.md`, `docs/design/PLAN.md`.

**Стан системи:** `dotnet --list-sdks`, `dotnet --list-runtimes`, `dotnet ef --version`, `dotnet user-secrets list`, `docker ps -a`, `docker volume ls`, `psql \dt` + `information_schema.columns` для `Masters`, NuGet flat-container API для 4 пакетів.

**Зовнішні:** [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), [breaking changes .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0), [ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview?view=aspnetcore-10.0), [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes), [міграція 9→10](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0), [EFCore.PG 10.0](https://www.npgsql.org/efcore/release-notes/10.0.html), [BCrypt.Net releases](https://github.com/BcryptNet/bcrypt.net/releases). Контент зовнішніх джерел перефразовано відповідно до ліцензійних обмежень.
