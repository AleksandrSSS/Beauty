# Блок C — вузол графіка: `ScheduleService` (Фаза 4)

Джерело: `masters-feature-plan.md` §10. Виконує роадмап P1-2 (`BookingValidator` = `ScheduleService`). Залежність: блок B.

## Мета

Одне джерело правил графіка для всіх споживачів. Виправляю D1–D4 тут, щоб блоки D і E будувались на правильному.

## Кроки

### C1. Нові файли

1. `server/Services/ScheduleService.cs`: `WindowDays = 14`, методи `GetSlotsAsync(masterId?, serviceId, ct)`, `GetMasterScheduleAsync(masterId, ct)`, `PreviewRotation(type, anchor)`, `ValidateAsync(masterId, serviceId, startUtc, ignoreAppointmentId?, ct)`.
2. `server/Models/ScheduleDtos.cs` (або в тому ж файлі): `SlotDto`, `SlotMasterDto`, `BusyIntervalDto`, `MasterDayDto`, `BookingCheck`.
3. `Program.cs`: `AddScoped<ScheduleService>()`.

### C2. Правила (§10.2, 10 пунктів)

Вікно `TodayInSalon → +14`; `RotationHelper.WorksOn`; `WorkingHours` по `DayOfWeek`, пропуск при `null`/`End<=Start`; крок 30 хв; конвертація тільки `clock.ToUtc`; `startUtc > UtcNow`; майстер `IsActive` + `MasterServices`; послуга `IsActive` (D4); зайнятість `Confirmed` АБО (`Pending` + `CreatedAt > -15хв`) однаково для слотів і валідації (D3); перетин `start < End && end > Start`.

### C3. Виправлення дефектів

1. D2 `RotationHelper.cs:18`: нормалізований залишок `(((.../ 7) % 2 + 2) % 2 == 0`. `TwoTwo` не чіпати.
2. D1 `CatalogController.cs:20`: інжектувати `SalonClock`, `DateOnly.FromDateTime(UtcNow)` → `clock.TodayInSalon`.
3. D8/D11 (seed, плаваючий якір) не чіпати — змінюють дані.

### C4. Переписати `BookingController`

1. Інжектувати `ScheduleService`.
2. `Slots` делегує в `GetSlotsAsync`, форму відповіді зберегти точно: з `masterId` → `[{start,end}]`, з `null` → `[{start,end,masters:[{id,name}]}]`.
3. `Create`: ручні перевірки → `ValidateAsync`, зберегти транзакцію `Serializable`, тексти помилок 1:1, коди 400/409/404.
4. `BookingWindowDays` видалити → `ScheduleService.WindowDays`.
5. Нові/змінені методи — з `CancellationToken`.
6. Нові ендпоінти з часом — тільки ISO-8601 з `Z`; клієнт передає `slot.start` як прийшов. Старий `reschedule` не чіпати (це C3).

### C5. Швидка перемога з Фази 10

`ReminderWorker.cs`: додати `&& a.Master.IsActive` у `Where` (1 рядок). Без цього шлються нагадування до звільнених майстрів.

## Перевірка блока (регресія — найважливіше)

1. `POST /api/booking/slots` з `masterId` — JSON тієї ж форми що до рефакторингу (зберегти до/після і порівняти).
2. Те саме з `masterId: null` включно з `masters` і сортуванням.
3. `Booking.tsx` в обох режимах: послуга → слоти → бронювання → код → підтвердження.
4. D3: свіжий `Pending` ховає слот 15 хв.
5. D4: деактивована послуга → 404/помилка, не 200 з порожнім масивом.
6. D2: `Weekly` з якорем у майбутньому рахує правильно.
7. Минуле і 15-й день відсутні.
8. `dotnet build server` без нових warning'ів.

Оцінка: 3–4 год.
