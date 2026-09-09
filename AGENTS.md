# AGENTS.md — інструкції для агентів у проєкті Beauty Salon

Актуально на **2026-09-03**. Мова відповідей і коментарів у коді — **українська**.

---

## 1. Про проєкт

Сайт салону (перукарня + манікюр): онлайн-запис до майстра з підтвердженням кодом, особистий кабінет клієнта, адмін-панель. Тестовий проєкт, не в продакшені.

Ключова доменна особливість: **майстри працюють за ротацією** — `Weekly` (тиждень через тиждень) або `TwoTwo` (2 дні зміна / 2 вихідні). Вікно бронювання — **28 днів (4 тижні)**.

---

## 2. Стек і структура

| Частина | Технології |
|---|---|
| Бекенд | ASP.NET Core **10** (`net10.0`), EF Core 10, PostgreSQL 16 у Docker, JWT |
| Клієнт | React 18, Vite 5, TypeScript 5.5, react-router-dom 6 |
| Тести | **відсутні** (тестового проєкту немає, покриття ≈ 0) |

```
server/
  Program.cs                 — DI, JWT, CORS, pipeline, виклик сіду
  appsettings.json           — конфіг (без секретів)
  Models/Entities.cs         — усі сутності в одному файлі
  Data/AppDbContext.cs       — DbContext + Seed (EnsureCreated!)
  Data/RotationHelper.cs     — правила ротації майстрів
  Controllers/               — Auth, Account, Admin, Booking, Catalog
  Services/                  — JwtService, SalonClock, NotificationService, ReminderWorker
client/src/
  api.ts                     — fetch-обгортки, токен, типізовані ендпоінти
  auth.tsx                   — AuthContext, useAuth()
  types.ts                   — DTO, що дзеркалять серверні відповіді
  styles.css                 — усі стилі (glassmorphism, темна тема)
  pages/                     — Home, Booking, Login, Cabinet, Admin
docs/design/                 — Figma-макети, PLAN.md ре-скіну, tokens.css (НЕ підключений)
docs/review/                 — аудит проєкту (00-04) + roadmap із пріоритетами P0/P1/P2
docs/review/Kiro/            — quick-wins-check.md, masters-feature-plan.md
```

---

## 3. Команди

```powershell
# БД (потрібен кореневий .env — його НЕМАЄ, docker compose без нього впаде)
docker compose up -d
docker exec beauty-db psql -U beauty -d beauty -c "\dt"

# Бекенд, порт 5000
dotnet build server
dotnet run --project server

# Клієнт, порт 5173
cd client; npm install; npm run dev
cd client; npm run build      # = tsc -b && vite build

# EF (працює лише після Фази 1 плану — див. §7)
dotnet ef migrations list --project server
```

Оточення розробника: SDK `10.0.400` / `10.0.204` / `9.0.306`; рантайми 8/9/10; `dotnet ef` CLI **10.0.8** (мажорно новіший за EF 9 у проєкті).

---

## 4. Критичні правила домену

Порушення будь-якого з них ламає систему тихо, без помилок компіляції.

### 4.1. Час

- У БД — **завжди UTC**. На UI — «настінний» час салону.
- Будь-яка конвертація — **тільки через `SalonClock`**: `ToUtc(day, time)`, `ToSalon(utc)`, `TodayInSalon`, `Format(utc)`. Таймзона з `Salon:TimeZone` (дефолт `Europe/Kyiv`).
- **Заборонено:** `DateOnly.FromDateTime(DateTime.UtcNow)` як «сьогодні» — на межі доби дає зсув на день. Використовуй `clock.TodayInSalon`.
- На клієнті **заборонено** `new Date(day + 'T12:00')` і `s.start.slice(0, 10)`. Форматувати через `Intl.DateTimeFormat('uk-UA', { timeZone: 'Europe/Kyiv', ... })`.
- **Салонну дату** зі UTC-мітки отримувати як `new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Kyiv' }).format(new Date(iso))` → `YYYY-MM-DD`. `toISOString().slice(0,10)` і `.slice(0,10)` дають **UTC**-дату і на межі доби помиляються на день.
- Нові ендпоінти, що приймають час, вимагають **ISO-8601 з `Z`**. Клієнт передає значення так, як отримав його від API, не переформатовуючи.

### 4.2. Ротація майстрів

- Єдине джерело — `RotationHelper.WorksOn(master, day)`. Не дублювати логіку ні в C#, ні в TypeScript.
- `RotationAnchor` семантика: **перший робочий день** (будь-який день тижня) для обох типів. `Weekly`: тиждень якоря (Пн–Нд) робочий з дня anchor до неділі, далі повні тижні через один; дні раніше anchor — не робочі. `TwoTwo` — 2/2 (`mod 4 < 2`).

### 4.3. Видалення = деактивація

- Послуги й майстри видаляються **м'яко** (`IsActive = false`), щоб не зламати історію записів. Hard-delete лише для сутностей без записів.
- Фільтр `IsActive` обов'язковий у публічних вибірках (`CatalogController`, слоти, `BookingController.Create`).

### 4.4. Захист від подвійного бронювання

`BookingController.Create` працює в транзакції `BeginTransactionAsync(IsolationLevel.Serializable)`. **Не прибирати і не знижувати рівень ізоляції.** Нотифікації надсилати **після** `CommitAsync()` — `NotificationService.SendAsync` робить власний `SaveChangesAsync`.

### 4.5. Контракт API з клієнтом

- JSON: `camelCase` + `JsonStringEnumConverter` (enum-и як **рядки**). Зміна цього ламає `client/src/types.ts`.
- Помилка: `{ "error": "текст українською" }`. Успіх без даних: `{ "ok": true }`.
- Коди: 400 — порушення правил, 401/403 — доступ, 404 — немає сутності, 409 — конфлікт зайнятості.
- `DateOnly` серіалізується як `"2026-09-03"`, `TimeOnly` — як `"09:00:00"` (клієнт робить `.slice(0, 5)`).

### 4.6. Авторизація

- Нові ендпоінти мають **явний** `[Authorize]`, `[Authorize(Roles = "Admin")]` або `[AllowAnonymous]`. Global auth policy зараз немає, і публічність тримається лише на її відсутності — це заплановано змінити.
- JWT-клейми: `NameIdentifier` (id), `MobilePhone` (телефон), `Name`, `Role`.
- Виняток (гостьове бронювання, 2026-09-08): `POST /api/booking/create` і `/confirm` свідомо `[AllowAnonymous]`. Гість передає телефон у тілі; сервер робить find-or-create `User` за телефоном і кладе його `Id` у `Appointment.ClientId`. `Confirm` видає JWT (автологін). `BookingController` більше не читає телефон із `MobilePhone`-клейму з `!` — NRE-ризик для гостя усунуто.

---

## 5. Конвенції бекенду

- C# 12+: primary constructors для контролерів і сервісів (`class Foo(Dep d) : ControllerBase`), file-scoped namespaces, `Nullable` enable, `ImplicitUsings` enable.
- DTO — позиційні `record` на початку файлу контролера. **Новий параметр додавати лише останнім** — зміна порядку ламає біндинг з клієнта.
- Індекси — у `AppDbContext.OnModelCreating`.
- Асинхронність — `async Task<IActionResult>`; додавати `CancellationToken` у нові методи.
- Логування — `ILogger<T>` зі структурованими параметрами (`"по запису {Id}"`), не інтерполяція.
- XML-комментарі `///` українською для неочевидної логіки.

## 6. Конвенції клієнта

- Функціональні компоненти, іменований default-export на сторінку.
- Доступ до користувача — **тільки** `useAuth()` з `auth.tsx`. Не імпортувати `user` напряму.
- Усі HTTP — через `api<T>()` з `api.ts`. Виняток: `multipart/form-data` (там `Content-Type` не ставити — boundary додає браузер).
- Помилки — через `errMsg(e)`, не `catch (e: any)`.
- Стилі — **лише** CSS-змінні з `styles.css`: `--accent`, `--glass-10/15/30`, `--radius`, `--shadow-soft`, `--text`, `--muted`, `--text-dim`. Класи `.card`, `.grid`, `.slot`, `.step`, `.table`, `.btn`, `.link`. `docs/design/tokens.css` **не підключений** — не покладатись на нього.
- `styles.css` тільки **доповнювати**, наявні правила не переписувати.
- Клікабельні картки — `<Link>`, не `<div onClick>` (клавіатура + `Ctrl+click` безкоштовно).
- A11y: `alt` у `<img>`, `:focus-visible` з видимим контуром, декоративні елементи `aria-hidden`.
- Контраст: `--text-dim` (`rgba(255,255,255,.6)`) на фото/склі — ймовірний провал WCAG AA. Для нового тексту використовуй `--text` або `--muted`.
- UI-тексти українською; статуси — людські лейбли, не сирі enum.

---

## 7. Стан схеми БД — читати перед будь-якою зміною моделі

**Міграції підключені** (Фаза 1 виконана 2026-09-04). Актуальний стан:

- `server/Data/DbInitializer.cs` — `db.Database.Migrate()` першим рядком, далі ідемпотентний сід. Викликається з `Program.cs`.
- `AppDbContext.Seed` **видалено** — сід живе тільки в `DbInitializer`.
- `server/Migrations/` — зберігається разом із кодом. Застосовані міграції (`__EFMigrationsHistory`, `ProductVersion 10.0.11`):
  | MigrationId | Що |
  |---|---|
  | `20260904105842_InitialCreate` | baseline наявної схеми (позначка вставлена вручну, DDL не виконувався) |
  | `20260904111100_AddMasterPhotoUrl` | `Masters.PhotoUrl text NULL` |
  | `20260908131936_RemoveWorkingHours_AddSalonSettings` | `DROP TABLE WorkingHours`, `CREATE TABLE SalonSettings` (години закладу 09:00–18:00, сід у `DbInitializer`) |
  | `20260908144143_AddMasterDayOverride` | `CREATE TABLE MasterDayOverrides` + unique `(MasterId, Date)`, FK Cascade |
- Контейнер `beauty-db` живий, том `beauty_beauty_pgdata` містить дані.

**Як додавати зміну моделі:**

```powershell
dotnet ef migrations add <ЗмістовнаНазва> --project server
dotnet ef migrations script --project server --output check.sql   # переглянути DDL
dotnet ef database update --project server
dotnet ef migrations list --project server                        # переконатись, що застосована
```

Тимчасові `check.sql` / дампи не залишати в проєкті.

**Заборонено:** `ALTER TABLE` руками, `EnsureCreated()`, перестворення тому (`docker compose down -v`), видалення чи редагування вже застосованих міграцій. `InitialCreate` — baseline: його `Up()` **ніколи не виконувався** на цій БД, тому відкат `database update 0` знищить дані. Не робити.

Сід ідемпотентний за умовами «немає адміна» / «немає послуг» — на заповненій БД він нічого не робить, тобто змінами в `Seed` наявні дані не оновиш.

---

## 8. Секрети і конфіг

- `Jwt:Key` і `Admin:Password` — **тільки в user-secrets** (`dotnet user-secrets set ... --project server`). Обидва вже задані. `Program.cs` кидає виняток без `Jwt:Key`, тому навіть `dotnet ef` не запуститься без секретів.
- **Не виводити значення секретів** у відповідях і логах — посилатися на ключ за назвою.
- `appsettings.json` — без секретів: connection string (localhost), `Jwt:Issuer/Audience/ExpireHours`, `Admin:Phone/Name`, `Cors:AllowedOrigins`, `Salon:TimeZone`, `Notifications:*`.
- `client/.env`: `VITE_API_URL=http://localhost:5000` — клієнт звертається **напряму**, не через Vite-проксі. Vite проксує лише `/api`.
- Нотифікації без налаштованих провайдерів працюють у **MOCK**: код пишеться в лог сервера і `NotificationLogs`, а в Development повертається в полі `devCode`.

---

## 9. Відомі дефекти — не «виправляти» мовчки

Усі задокументовані. Виправляти **лише** якщо це частина поточної задачі або користувач попросив.

| Де | Суть |
|---|---|
| `Admin.tsx` | форма майстра **не має полів** `bio` та `isActive` → повернути звільненого майстра з UI неможливо. Виправляється у Фазі 9 |
| `Admin.tsx:1` | BOM/кодування; за `PLAN.md` — мохібейк кирилиці. **Перевіряти кодування перед правкою** |
| `Home.tsx:11-13` | fetch без `catch`, `loading`, скасування. Виправляється у Фазі 7 |
| `AdminController.Reschedule` | не валідує перетини/години/ротацію і самовільно піднімає `Pending → Confirmed` (C3). Тепер є `ScheduleService.ValidateAsync` — використати при правці |
| `AdminController.Appointments` | без пагінації, `Include`×3 + повний `ToList()` (M9/P1-6) |
| `DbInitializer` (сід) | `RotationAnchor` залежить від дня першого запуску (M11); `(DayOfWeek)d` де `0 = Sunday`. Перенесено з `AppDbContext` **без** зміни семантики |
| `AuthController.cs:89` | `adminPhone` — dead code |
| `AdminController.UploadPhoto` | історія: `ReadExactlyAsync` давав 500 на файлі < 12 байт. **Виправлено 2026-09-04** — читаємо через `ReadAsync` і перевіряємо кількість байт |
| кореневий `.env` | відсутній, хоч `docker-compose.yml` має `env_file: [.env]` → `docker compose up` впаде |

**Виправлено блоком C (2026-09-04) — не «фіксити» вдруге:**

- `CatalogController` використовує `clock.TodayInSalon` замість `DateTime.UtcNow` (D1);
- `RotationHelper` Weekly-гілка: парність календарного тижня від тижня якоря + гард `day >= anchor` (D2; 2026-09-09, weekly-rotation-anchor-fix, третя редакція плану). Історія: 7/7-блоки від anchor давали «сім днів поспіль» всупереч вимозі; тепер неповний перший тиждень (anchor..Нд) + повні тижні через один; `MondayOf` повернено; якір зберігається як є, дефолт форми — `todaySalon()`; `worksThisWeek/NextWeek` = робочий день у найближчі 7 / наступні 7 днів;
- зайнятість слота однакова в усіх шляхах: `Confirmed` **або** свіжий `PendingVerification` (15 хв) — `ScheduleService.PendingHold` (D3);
- слоти перевіряють `Service.IsActive` (D4);
- `ReminderWorker` фільтрує `a.Master.IsActive`.

---

## 10. Активний план змін

**Головний документ: `docs/review/Kiro/masters-feature-plan.md`** (1600 рядків, 13 фаз, ~22–26 год). Перед роботою над майстрами, фото, слотами, міграціями чи .NET-версією — читати його, а не планувати заново.

Що план змінює (стисло):

| Фаза | Зміна | Ключове для агента |
|---|---|---|
| 0 | `net9.0` → **`net10.0`**, EF/JwtBearer → 10.x, BCrypt → 4.2.1 | ✅ виконано 2026-09-04 (блок A) |
| 1 | `EnsureCreated()` → **Migrations через baseline**, сід → `DbInitializer` | ✅ виконано 2026-09-04 (блок A) — див. §7 |
| 2 | `Master.PhotoUrl` | ✅ виконано 2026-09-04 (блок B) |
| 3 | Upload фото у `server/wwwroot/uploads/masters`, `UseStaticFiles()`, `Uploads:*` у конфізі | ✅ виконано 2026-09-04 (блок B). Файли **не зберігаються**; бекап двокроковий |
| 4 | Логіка слотів → **`server/Services/ScheduleService.cs`**; виправлення 4 дефектів графіка | ✅ виконано 2026-09-04 (блок C). Правила графіка живуть **тільки** там — див. §9 |
| 5 | `GET /api/catalog/masters/{id}` і `.../schedule` | — |
| 6–8 | `MasterCard`, `BookingCalendar`, сторінка `/masters/:id`, календар у `Booking.tsx` | Нова папка `client/src/components/`; вибір часу стає двоетапним (дата → слоти) |
| 9 | Адмінка: фото, `bio`, `isActive` | Закриває два дефекти з §9 |
| 10 | Звільнення майстра: `deactivation-impact`, `deactivate`, переназначення записів, фільтр у `ReminderWorker` | — |
| 11 | Прев'ю ротації, валідація повноти даних майстра | — |
| 12 | Наскрізна перевірка | — |

**Порядок жорсткий:** 0 → 1 → 2 → 3 → 4 → далі 5–8 / 9–11 паралельно → 12. Фаза 4 — вузол: 5, 10, 11 залежать від `ScheduleService`.

### Узгодження з роадмапом

`docs/review/04-roadmap.md` містить задачі, які **перетинаються** з планом — не робити двічі:

- **P1-1** (EF міграції + `DbInitializer`) = Фаза 1.
- **P1-2** (єдиний компонент правил, у роадмапі названий `BookingValidator`) = Фаза 4, реалізується як **`ScheduleService`**.
- **C7** = Фаза 1. **M4**, **M6** = Фаза 4. **C3** частково = Фаза 10.

Поза планом лишаються (не братися без запиту): `P0-1` (`CodeHash`/`AttemptCount`), `P0-2` (rate limiting), `P0-4` (`RequestId`), `P0-6` (`RefreshTokens`), `C4` (`AuditLog` + матриця статусів), `M8` (FluentValidation), `M9` (пагінація), `M12` (`UseExceptionHandler`, security headers), Dockerfile, CI, тести, підключення `tokens.css`.

---

## 11. Заборони

- ❌ Не створювати й не видаляти файли «для порядку», не прибирати тимчасові артефакти без запиту.
- ❌ Не додавати `global.json` — рішення користувача: гнучкість, беремо найвищий встановлений SDK.
- ❌ Не створювати `Dockerfile` і не додавати сервіс `server` у `docker-compose.yml` — відкладено свідомо.
- ❌ Не зберігати завантажені фото (`server/wwwroot/uploads/masters/*`) і дампи БД (`db-backup-*.sql`) у проєкті.
- ❌ Не запускати `docker compose down -v` чи `DROP TABLE` — том містить робочі дані.
- ❌ Не додавати інтерактивні флаги.
- ❌ Не додавати автотести без запиту (тестового проєкту немає — його створення це окреме рішення).
- ❌ Не переписувати наявні правила в `styles.css` — тільки доповнювати.
- ❌ Не міняти форму відповіді `POST /api/booking/slots` — `Booking.tsx` на неї спирається побайтово.
- ❌ Не додавати нових ендпоінтів для календаря — доступні дати виводяться з наявних `POST /api/booking/slots` і `GET /api/catalog/masters/{id}/schedule` (R15).
- ❌ Не розширювати вікно бронювання за межі `ScheduleService.WindowDays` (28 днів / 4 тижні) і не додавати навігацію по місяцях у календарі.
- ✅ Виняток із «суто клієнтських правок бронювання» (`master-schedule-booking-fix.md` §7): гостьове бронювання з одним кодом реалізовано на сервері (2026-09-08, `guest-booking-single-code-plan.md`) — `create`/`confirm` `[AllowAnonymous]`, телефон у тілі, find-or-create клієнта, `confirm` видає JWT; `create` повертає `confirmed`, залогінений бронює без коду одразу в `Confirmed`.
- ✅ Графік закладу (2026-09-08, `salon-hours-and-master-schedule-plan.md` п.1–3,5,6): години єдині на всі дні — сутність-одинак `SalonSettings` (`OpenTime`/`CloseTime`, сід 09:00–18:00 у `DbInitializer`), `WorkingHour` видалено з моделі й БД; `ScheduleService` читає години закладу; ендпоінти `GET/PUT /api/admin/salon-hours`; блок годин прибрано з форми майстра, в адмінці вкладка «Графік закладу». Ендпоінт `toggle-rotation` і його кнопка видалені.
- ✅ Відхилення днів майстра (2026-09-08, `master-day-override-plan.md`): сутність `MasterDayOverride` (`MasterId`, `Date`, `IsWorking`, `Start?`/`End?`, unique `(MasterId, Date)`, FK Cascade); пріоритет override > (ротація + заклад) через `ScheduleService.EffectiveDay` (слоти, графік, валідація; `PreviewRotation` без змін); ендпоінти `GET/PUT/DELETE /api/admin/masters/{id}/day-override(s)`; `BookingCalendar` режим `mode='edit'` + `dayStates`; редактор у формі збереженого майстра (новому — прев'ю ротації).

---

## 12. Перед тим як сказати «готово»

1. `dotnet build server` — без нових warning'ів.
2. `cd client; npm run build` — tsc + vite без помилок.
3. Якщо торкався авторизації або пакетів — `POST /api/auth/admin-login` повертає 200 (перевірка, що BCrypt-хеш адміна валідний).
4. Якщо торкався слотів — обидва режими `POST /api/booking/slots` (`masterId` заданий і `null`) дають ту саму форму відповіді, і повний цикл бронювання в `Booking.tsx` проходить.
5. Якщо торкався схеми — `dotnet ef migrations list` и перевірка колонок у psql.
6. Якщо змінював поведінку з часом — перевірити на межі доби (23:30 за Києвом) и на обох типах ротації.
7. Стверджувати «працює» лише про те, що реально запускав. Не перевірене — називати не перевіреним.
