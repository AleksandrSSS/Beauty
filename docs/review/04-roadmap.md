# 04 — Roadmap: що робити і в якому порядку

## P0 — безпека і блокери прода (1–2 тижні)

| # | Задача | Де | Оцінка |
|---|--------|----|--------|
| P0-1 | OTP 6 цифр, хеш SHA256+salt, спроби ≤5, expiry 5хв, cleanup фоном | `AuthController`, `BookingController`, `VerificationCode` | 1д |
| P0-2 | Rate-limit `request-code` 5/10хв, `admin-login` 5/15хв + lockout, `AddRateLimiter` | `Program.cs` | 0.5д |
| P0-3 | `devCode` тільки в `Development`, інакше `null`; MOCK-логи без коду | `Auth/BookingController`, `NotificationService:40` | 0.5д |
| P0-4 | Прив'язка `request→verify` по `RequestId`, не перезаписувати чужий профіль | `AuthController:62-79` | 1д |
| P0-5 | `Reschedule/SetStatus`: матриця переходів + перевірка перетинів/графіка + audit | `AdminController:151-175` | 1-2д |
| P0-6 | JWT: перевірка довжини ключа ≥32 байт, access 15хв + refresh в httpOnly, revocation | `JwtService`, `Program.cs` | 1-2д |
| P0-7 | Секрети з проєкту в env/user-secrets, `.env.example` без значень | `appsettings.json`, compose | 0.5д |

Патч-приклад P0-3:
```csharp
var isDev = app.Environment.IsDevelopment();
return Ok(new { mock = result.IsMock, devCode = (isDev && result.IsMock) ? code : null });
```

Патч-приклад P0-5 (матриця):
```csharp
static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> Allowed = new() {
  [PendingVerification] = [Confirmed, Cancelled],
  [Confirmed] = [Cancelled, Completed],
  [Cancelled] = [], [Completed] = [],
};
```

## P1 — коректність логіки (2–3 тижні)

| # | Задача | Оцінка |
|---|--------|--------|
| P1-1 | EF міграції + `DbInitializer`, прибрати `EnsureCreated` | 1д |
| P1-2 | Єдиний `BookingValidator` для `Slots/Create/Reschedule` (перетини, `IsActive`, `MasterService`, вікно 14д) | 2д |
| P1-3 | Час тільки через `SalonClock`: сід, `Catalog`, фронт ISO-Z, `ToUtc` для reschedule | 1д |
| P1-4 | `ReminderWorker`: `ReminderSentAt` тільки при `Success`, `RetryCount`, індекс | 0.5д |
| P1-5 | Валідація всіх DTO (`Required/Phone/Range`), `UseExceptionHandler`, ProblemDetails | 1д |
| P1-6 | Пагінація `admin/appointments` + `account/appointments` (`page/pageSize/from/to`) | 1д |
| P1-7 | Фронт auth: `AuthContext + ProtectedRoute + 401→login`, фікс `+''→0`, `AbortController`, `returnUrl` | 2-3д |
| P1-8 | Уніфікувати порти/env (`5000`, `VITE_API_URL`, `Host=postgres`), `chcp 65001` в скриптах | 0.5д |

## P2 — prod-ready і якість (3–4 тижні)

* Dockerfile multi-stage `sdk:10.0 → aspnet:10.0` + `HEALTHCHECK /health`, client `build → nginx`, compose 3 сервіси.
* CI `dotnet build/test + npm build + e2e + playwright`.
* xUnit: double-book паралельно, expiry, reschedule-конфлікт; vitest: слоти, селекти.
* Фронт: `components/hooks`, форми з `disabled/pattern`, контрольований `datetime-local`, укр. статуси, пошук/фільтри, `overflow-x` таблиць, `label+aria-live+focus-visible`, `ErrorBoundary`, `vite sourcemap`.
* Бек: Swagger, `CancellationToken`, timeouts+Polly для `HttpClient`, Outbox для нотифікацій, `Logging Warning` в prod.
* Підключити `tokens.css`, видалити `dist/node_modules` з проєкту, `requirements.txt`, UTF-8 всюди.

## Швидкі перемоги (сьогодні, <2 год)

1. `devCode` за `IsDevelopment` — 5 хв.
2. `.env` + порт `5000` + прибрати `VITE_AUTH_URL` — 10 хв.
3. `+'' → null` в двох селектах `Booking.tsx:73,80` — 10 хв.
4. `reminders: if Success` в `ReminderWorker.cs:46` — 5 хв.
5. `restart: unless-stopped + env_file` в compose — 10 хв.
6. `chcp 65001` в `test-e2e.ps1` + `ui-tests` — 10 хв.
