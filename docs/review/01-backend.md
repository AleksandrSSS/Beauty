# 01 — Backend: детальний розбір

## 1.1. Архітектура — що добре

* `Program.cs:10` — `AddDbContext + UseNpgsql`, конфіг через `GetConnectionString`.
* `Program.cs:18-31` — JWT з `ValidateIssuer/Audience/Lifetime/SigningKey`, правильно.
* `BookingController.cs:69` — `BeginTransaction(IsolationLevel.Serializable)` проти подвійного бронювання. Рідко бачу в MVP, тут — є.
* `SalonClock.cs:11-33` — централізована TZ: `ToUtc(day,time)`, `ToSalon(utc)`, `TodayInSalon`. Ідея вірна: в БД UTC, на UI «настінний» час.
* `AppDbContext.cs:19-21` — унікальний індекс `User.Phone`, композит `MasterService`, індекс `Appointment(MasterId,StartTime)`.
* `AdminController.cs:49,109-114` — м'яке видалення послуг/майстрів з історією.
* Пароль адміна — `BCrypt.HashPassword/Verify`, generic `Unauthorized` без енумерації юзерів.

## 1.2. Critical

### C1. OTP brute-force — `AuthController.cs:18,27,52`, `BookingController.cs:102,130`

```csharp
public const string CodeChars = "123456789";
var code = new string(Enumerable.Range(0,4).Select(_ => CodeChars[Random.Shared.Next(9)]).ToArray());
```

* Простір: `9^4 = 6561`. 10 хв життя `ExpiresAt = UtcNow.AddMinutes(10)`.
* Нема `AddRateLimiter`, lockout, капчі, затримки. `Program.cs` взагалі без rate-limit.
* Атака: цикл `POST /api/auth/verify-code {phone, code: 1111..9999}` — 6561 запитів це ~2-5 хв на одному потоці. Те саме на `POST /api/booking/confirm`.
* Плюс `RequestCode` без ліміту — OTP-спам на чужий номер = ваші гроші за Twilio/SMS.

Фікс:
```csharp
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("otp", x => {
  x.PermitLimit = 5; x.Window = TimeSpan.FromMinutes(10); x.QueueLimit = 0;
}));
// + VerificationCode: AttemptCount, блокувати після 5 невдалих, код 6 цифр з 0-9, хешувати
```

### C2. Перезапис чужого профілю — `AuthController.cs:62-79`

```csharp
var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone.Trim());
if (user == null) { /* create */ }
else {
  if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name.Trim();
  if (!string.IsNullOrWhiteSpace(dto.Contact)) user.ExternalContact = dto.Contact;
  user.PreferredChannel = vc.Channel.ToString();
}
```

Сценарій: знаю телефон жертви → `request-code` на свій Telegram → підбираю/перехоплюю код → `verify-code {phone: жертви, name: "Хакер"}` → профіль жертви перезаписано, сесія на мене. Нема прив'язки `запит → верифікація` по `sessionId/device`.

Фікс: `VerificationCode.RequestId (guid)`, повертати його в `request-code`, вимагати в `verify-code`; не міняти `Name/Contact` без авторизованої сесії власника.

### C3. Admin reschedule обходить все — `AdminController.cs:151-163`

```csharp
var start = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Utc);
appt.EndTime = start.AddMinutes(appt.Service.DurationMin);
appt.StartTime = start;
if (appt.Status == PendingVerification) appt.Status = Confirmed;
```

Немає: перевірки перетинів, `WorkingHours`, `RotationHelper.WorksOn`, вікна 14 днів, `start > now`, `MasterService`. Можна покласти два записи один на один і «підтвердити» без коду.

Фікс: винести валідацію з `BookingController.Create` в `BookingValidator.Validate(master,service,start)` і перевикористати тут + в транзакції `Serializable`.

### C4. Довільні переходи статусів — `AdminController.cs:165-175`

```csharp
if (!Enum.TryParse<AppointmentStatus>(dto.Status, true, out var status)) ...
appt.Status = status;
```

Можна `Completed → Pending`, `Cancelled → Confirmed` без правил і без аудиту. Потрібна матриця: `Pending→{Confirmed,Cancelled}`, `Confirmed→{Cancelled,Completed}`, `Cancelled→{}` тощо + таблиця `AuditLog {who, what, when}`.

### C5. `devCode` в API — `AuthController.cs:47`, `BookingController.cs:116`

```csharp
return Ok(new { mock = result.IsMock, devCode = result.IsMock ? code : null });
```

В `appsettings.json` всі провайдери пусті → `IsMock=true` завжди → код в HTTP-відповіді. Задумано для dev (`README:54`), але в проді з неналаштованим SMS це повний обхід OTP. Будь-який проксі-лог зіллє коди.

Фікс:
```csharp
devCode = app.Environment.IsDevelopment() && result.IsMock ? code : null
```

### C6. Коди plaintext + в логах — `Entities.cs:90`, `NotificationService.cs:35,40-43`

```csharp
db.NotificationLogs.Add(new NotificationLog { Message = message /* "...: 1234" */ });
logger.LogWarning("[{Label} MOCK] -> {Recipient}: {Message}", ...);
```

Код лежить в `VerificationCodes.Code`, в `NotificationLogs.Message`, в `server_run.log`. Достатньо доступу до бекапу/логу.

Фікс: зберігати `CodeHash = SHA256(code+salt)`, порівнювати по хешу; в `NotificationLog` писати без коду (`"код надіслано, id=..."`); MOCK-лог тільки в `Development`.

### C7. `EnsureCreated()` замість міграцій — `AppDbContext.cs:26`

Міграцій нема взагалі. Будь-яка зміна моделі в проді = або дроп, або ручний SQL. `dotnet ef migrations add Init; dotnet ef database update`, `Seed` винести в `DbInitializer` з ідемпотентністю.

## 1.3. Major

**M1. Секрет БД у відкритому вигляді** `appsettings.json:2-4` `Password=beauty`. Винести в `user-secrets/env`, у проєкті залишити тільки плейсхолдер.

**M2. Нема rate-limit на `admin-login`** `AuthController.cs:84-93` — брутфорс `admin123` без lockout. Додати `lockout after 5 fails + delay 2s`.

**M3. JWT 24г без refresh** `JwtService.cs:19-21`, `appsettings.json:5-9`. Клієнтська сесія по OTP живе добу, відкликати не можна. Нема перевірки довжини `Jwt:Key` (короткий ключ → слабкий HMAC), `ClockSkew` дефолтні 5 хв. Фікс: access 15 хв + refresh 7 д в httpOnly cookie з таблицею `RefreshTokens`.

**M4. `Slots != Create`** `BookingController.cs:33-37 vs 88-91`:
- `Slots` враховує тільки `Confirmed`, `Create` ще `Pending 15хв` → UI показує «вільно», API дає `409`.
- Обидва не перевіряють `Service.IsActive` і `MasterService` (майстер пропонує чужі послуги).
Фікс: один `GetBusyIntervals()` + `JOIN MasterServices`.

**M5. Час трактується як UTC** `BookingController.cs:75`, `AdminController.cs:156`:
```csharp
var start = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Utc);
```
Якщо фронт шле `"2026-09-05T10:00"` (без Z, «настінний» час), запис зсунеться на +2/+3г. Фронт має слати ISO з Z, або бекенд має приймати `DateOnly+TimeOnly` + конвертувати через `SalonClock.ToUtc`.

**M6. Змішування UTC/салонного** `AppDbContext.cs:53-54`, `CatalogController.cs:20`:
```csharp
var today = DateOnly.FromDateTime(DateTime.UtcNow); // треба SalonClock.TodayInSalon
```
На межі доби (23:30 UTC = 01:30 Kyiv) графік поїде на день. Те саме `MondayOf` vs UTC.

**M7. `ReminderWorker` втрачає нагадування** `ReminderWorker.cs:45-49`:
```csharp
var result = await notifier.SendAsync(...);
a.ReminderSentAt = DateTime.UtcNow; // навіть якщо Success=false
```
Потрібно `if (result.Success) a.ReminderSentAt = ... else a.RetryCount++`. Вікно `±10хв` при інтервалі `5хв` — ок, але при даунтаймі 20хв записи пропустяться назавжди.

**M8. Нема валідації DTO** `AuthController.cs:10-12`, `AccountController.cs:10`, `AdminController.cs:9-13`:
- `dto.Phone.Trim()` → `NullReferenceException → 500` при відсутньому полі. Records без `[Required/Phone/Length]`.
- `Price/DurationMin/Category/ServiceIds/WorkingHours` без перевірок → `500` по FK або `DurationMin=0/-100`.
Фікс: `FluentValidation` або DataAnnotations + `app.UseExceptionHandler`.

**M9. `Admin/appointments` без пагінації** `AdminController.cs:138-149` — `Include×3 + ToList()` всієї таблиці. При 10k записів — OOM/DoS. Потрібні `?from&to&page&pageSize&masterId&status`.

**M10. `VerificationCodes` не чистяться**, без індексів `Entities.cs:86-96`. Ріст таблиці, можливий reuse. Потрібен фоновий cleanup + `HasIndex(v => new {v.Phone, v.Purpose, v.ExpiresAt})`.

**M11. Сід ротації крихкий** `AppDbContext.cs:57-87`: `RotationAnchor=today/today+2/CurrentMonday` залежить від дня деплоя. Дві інсталяції дадуть різні графіки. Якір має бути фіксованою датою (напр. `2026-01-05`).

**M12. Нема `UseExceptionHandler/Https/HSTS/headers`** `Program.cs:51-65`. Неконтрольовані `500` з stacktrace, нема security headers. Додати `UseExceptionHandler("/error") + UseHsts + UseHttpsRedirection` і middleware security headers.

## 1.4. Minor

* `JwtService.cs:26-27` `GetUserId` кидає `FormatException→500` при битій claims → `TryParse + Unauthorized`.
* `Program.cs:33` `AddHttpClient()` без timeout/policy — Telegram/Twilio можуть висіти. `SetHandlerLifetime + Timeout 10s + Polly retry`.
* `NotificationService.cs:22` `Scoped + SaveChanges` всередині `SendAsync` — при виклику в зовнішній транзакції дасть неконсистентність. Краще `Outbox` або окремий scope.
* `RotationHelper.cs:18` Weekly до якоря: `(neg / 7) % 2` в C# дає від'ємний залишок → невірні дні в минулому. Обгорнути `((x % 2)+2)%2`.
* `AppDbContext.cs:97` `(DayOfWeek)d` — `0=Sunday`. Зараз нешкідливо (всі дні 9-19), але при диференціації дасть зсув на 1.
* CORS `AllowAnyHeader/AllowAnyMethod` + фолбек `localhost:5173` `Program.cs:40-43`. Публічність `slots/catalog` тримається лише на відсутності global auth — додати явні `[AllowAnonymous]` там і `[Authorize]` за дефолтом.
* Нема Swagger (`Swashbuckle` відсутній в csproj), нема `CancellationToken` в контролерах, нема запит-логів.
