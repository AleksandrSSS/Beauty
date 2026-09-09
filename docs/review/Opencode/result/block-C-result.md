# Результат блока C — вузол графіка (Фаза 4). Статус: ✅ ВИКОНАНО

Дата: 2026-09-04. План: `docs/review/Kiro/masters-feature-plan.md` §10. Закриває роадмап P1-2, дефекти D1–D4.

## C1. Нові файли

- `server/Models/ScheduleDtos.cs`: `SlotDto`, `SlotMasterDto`, `BusyIntervalDto`, `MasterDayDto`, `BookingCheck (+StatusCode)`, `RotationPreviewDto`.
- `server/Services/ScheduleService.cs`: `WindowDays = 14`, `GetSlotsAsync(masterId?, serviceId)`, `GetMasterScheduleAsync`, `PreviewRotation` (без БД), `ValidateAsync (+ignoreAppointmentId)`.
- `Program.cs`: `AddScoped<ScheduleService>()`.
- Правила §10.2 (10 пунктів): вікно, ротація, години, крок 30 хв, тільки `clock.ToUtc`, `startUtc > now`, `IsActive` + `MasterServices`, послуга `IsActive`, зайнятість `Confirmed` АБО свіжий `Pending` (15 хв) однаково всюди, перетин.

## C2. Виправлення дефектів

- **D2** `RotationHelper.cs:18`: нормалізований залишок `(((x / 7) % 2 + 2) % 2`. `TwoTwo` не чіпав.
- **D1** `CatalogController.cs:20`: `DateOnly.FromDateTime(UtcNow)` → `clock.TodayInSalon` (контролер отримав `SalonClock`).

## C3. BookingController через сервіс

- `Slots` делегує в `GetSlotsAsync`; форму збережено: з `masterId` → `[{start,end}]`, без → `[{start,end,masters:[{id,name}]}]`.
- `Create`: ручні перевірки → `ValidateAsync` всередині `Serializable`-транзакції; тексти помилок 1:1, коди 400/404/409 мапляться з `BookingCheck.StatusCode`.
- `BookingWindowDays` видалено → `ScheduleService.WindowDays`; `Slots`/`Create` з `CancellationToken`.
- Рядок `devCode`-гейту (чужа зміна, P0-3) не чіпав.

## C4. Швидка перемога з Фази 10

- `ReminderWorker`: `&& a.Master.IsActive` (1 рядок).

## Регресія §10.6 — 8/8 ✅

1. `slots masterId=1` до/після — **хеші JSON ідентичні** (`BD14F6...`), 140 слотів.
2. `masterId=null` — 286 слотів з `masters:[{id,name}]`, сортування за `start`.
3. Цикл `create → confirm → cancel` — OK (тестові сліди видалено; БД 6/6/2/3).
4. **D3**: свіжий `Pending` ховає слот (140→139), раніше показувався + 409.
5. **D4**: деактивована послуга → **404** (відновлено `IsActive=true`).
6. D2-якір у майбутньому: логіка нормалізована; повна перевірка — у блоці F на прев'ю.
7. Минуле і 15-й день у видачі відсутні (вікно `today..+14`, `startUtc > now`).
8. `dotnet build` 0 Warning; `npm run build` OK.

## Важливе спостереження

Файл `BookingController.cs` на диску виявився новішим за моє перше читання: multi-master режим `slots` і `devCode`-гейт по `ASPNETCORE_ENVIRONMENT` уже хтось додав (паралельна сесія, git-репо нема — звірити неможливо). Перечитав файл цілком перед правками, чужі зміни збережено.

## Файли (змінено/створено)

Створено: `Services/ScheduleService.cs`, `Models/ScheduleDtos.cs`.
Змінено: `Controllers/BookingController.cs` (Slots+Create), `Controllers/CatalogController.cs` (D1), `Data/RotationHelper.cs` (D2), `Services/ReminderWorker.cs` (IsActive), `Program.cs` (реєстрація).

## Наступний крок

Блок D (Фази 5–8): API майстра + типи + `MasterCard` + `/masters/:id`.
