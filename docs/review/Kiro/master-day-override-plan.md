# План: індивідуальні відхилення графіка майстра (day override) — клікабельний календар-редактор в адмінці

Актуально на **2026-09-08**. Це деталізація **пункту 4** з `salon-hours-and-master-schedule-plan.md`
(§2, «індивідуальні відхилення майстра»), який був свідомо винесений в окремий документ (§7 п.3 того плану)
і **не реалізований**. Пункти 1–3 (єдиний графік закладу `SalonSettings`), 5, 6 того плану — вже виконані.

> ⚠️ Це **план**, а не реалізація. Нічого не застосовано. Реалізовувати лише після команди «виконуй».
> Це **серверна зміна з новою таблицею і міграцією** + логіка графіка + UI. Виходить за межі «суто клієнтських»
> правок і за §11 AGENTS.md — після реалізації оновити AGENTS.md.

---

## 0. Вимога (дослівно від користувача)

В адмін-панелі при **кліку на день у календарі** майстра можна редагувати його робочі дні та години:
- зробити робочий день **коротшим** (інші години, ніж у закладу — «пів дня»);
- зробити робочий день (за ротацією) **вихідним**;
- зробити вихідний (за ротацією) день **робочим** — повністю або частково.

Тобто **виняток (override) поверх** «ротація + графік закладу» для конкретного майстра на конкретну дату.

---

## 1. Поточний стан (факти з коду — перевірено)

- **Override відсутній** повністю: `MasterDayOverride` / `day-override` — 0 збігів у `server/` і `client/`.
- Графік майстра = `RotationHelper.WorksOn(master, day)` (ротація) + години закладу з `SalonSettings`
  через `ScheduleService.GetHoursAsync` (фолбек 09:00–18:00).
- Три точки, де застосовуються «працює/години» у `server/Services/ScheduleService.cs`:
  1. `GetSlotsAsync` (~63–90): `if (!RotationHelper.WorksOn(master, day)) continue;` далі слоти в межах `open`–`close`.
  2. `GetMasterScheduleAsync` (~103–133): `working = RotationHelper.WorksOn(master, day) && close > open;`
     повертає `MasterDayDto(day, working, open?, close?, busy[])`.
  3. `ValidateAsync` (~150–190): `if (!RotationHelper.WorksOn(master, day)) ...`; далі перевірка меж `open`/`close`.
  - `PreviewRotation` (~141) — прев'ю для ще не збереженого майстра (без БД); override тут **не застосовуємо**
    (це прев'ю чистої ротації у формі).
- **Календар в адмінці** (`Admin.tsx`) використовує `BookingCalendar` у режимі прев'ю ротації
  (`previewRotation` → `rotPreview`), тобто **readOnly**. На скріншоті клік по дню лише показує тултип
  «Вихідний» — редагування немає.
- `BookingCalendar.tsx` жорстко робить `disabled={!available || readOnly}`. Тобто **у поточному вигляді
  він не придатний для редактора**, де треба клікати і по робочих, і по вихідних днях. Це впливає на рішення §6.
- DTO графіка: `MasterDayDto(DateOnly Date, bool IsWorking, TimeOnly? Start, TimeOnly? End, List<BusyIntervalDto> Busy)`
  (`server/Models/ScheduleDtos.cs`). `Master { RotationType, RotationAnchor, ... }` (`Entities.cs`).

---

## 2. Модель даних (ПОТРЕБУЄ ПІДТВЕРДЖЕННЯ — §9.1)

Нова сутність-виняток на дату для майстра:

```csharp
/// <summary>Індивідуальне відхилення графіка майстра на конкретну дату (перекриває ротацію + графік закладу).</summary>
public class MasterDayOverride
{
    public int Id { get; set; }
    public int MasterId { get; set; }
    public Master Master { get; set; } = null!;
    public DateOnly Date { get; set; }
    /// <summary>true — майстер працює цю дату; false — вихідний (перекриває ротацію).</summary>
    public bool IsWorking { get; set; }
    /// <summary>Години override. null/null при IsWorking=true → повний день за графіком закладу.</summary>
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}
```

Семантика (пріоритет **override > (ротація + графік закладу)**):
| Запис override | Ефект на дату |
|---|---|
| немає | стандартно: `WorksOn(ротація)` + години закладу |
| `IsWorking=false` | **вихідний**, навіть якщо ротація каже «робочий» |
| `IsWorking=true, Start=End=null` | **робочий** повний день за годинами закладу (навіть якщо ротація каже «вихідний») |
| `IsWorking=true, Start/End задані` | **робочий** з цими годинами («пів дня»), перекриває і ротацію, і години закладу |

- Навігація: додати `Master.DayOverrides` (`List<MasterDayOverride>`), або тримати без зворотної навігації —
  узгодити (§9.1). Індекс **unique `(MasterId, Date)`** (одна override на дату на майстра).
- Зберігати лише override у **вікні бронювання** (сьогодні … +28 днів)? Чи дозволяти будь-яку дату?
  Пропозиція: приймати будь-яку валідну дату в межах вікна (поза вікном сенсу немає — слотів там нема). Узгодити.

---

## 3. Валідація override (серверні правила)

При збереженні `IsWorking=true, Start/End`:
- `Start < End`;
- години override у межах **годин закладу** (`open ≤ Start`, `End ≤ close`)? Чи дозволяти виходити за межі
  закладу (напр. майстер починає раніше за заклад)? **Питання §9.2.** Пропозиція: **в межах закладу**
  (заклад зачинено — слотів не буде однаково), простіша й безпечніша семантика.
- дата в межах вікна (`today … today+WindowDays`).
- Кратність слотів (крок 30 хв) — не вимагати; генератор слотів сам вирівняє (крок 30 хв від `Start`).

`IsWorking=false` — годин не потребує (ігноруються).

---

## 4. Застосування override у `ScheduleService` (єдине джерело правил, §4.4 AGENTS.md)

Додати приватний хелпер, що для майстра+дати повертає ефективний графік дня:

```csharp
// Ефективний графік дня майстра з урахуванням override.
// Повертає (works, dayOpen, dayClose). Джерело правди годин — override або графік закладу.
private (bool Works, TimeOnly Open, TimeOnly Close) EffectiveDay(
    Master master, DateOnly day, TimeOnly salonOpen, TimeOnly salonClose,
    IReadOnlyDictionary<DateOnly, MasterDayOverride> overrides)
```

Логіка:
- якщо є override на `day`:
  - `IsWorking=false` → `(false, _, _)`;
  - `IsWorking=true` → `(true, Start ?? salonOpen, End ?? salonClose)`;
- інакше → `(RotationHelper.WorksOn(master, day), salonOpen, salonClose)`.

Точки застосування (замінити пряме `WorksOn` + `open/close`):
1. **`GetSlotsAsync`** — завантажити override майстра(ів) на вікно одним запитом у словник
   `Dictionary<(MasterId,Date), ...>` (для `masterId==null` — по всіх майстрах послуги). У циклі днів
   для кожного майстра брати `EffectiveDay`; замість `open`/`close` використовувати `dayOpen`/`dayClose`.
2. **`GetMasterScheduleAsync`** — так само; `MasterDayDto.Start/End` тепер відображають години override.
   Форма DTO **не змінюється** (клієнт `MasterSchedule.tsx` не ламається).
3. **`ValidateAsync`** — завантажити override майстра на `day`; замість `WorksOn` і меж закладу
   застосувати `EffectiveDay`. Помилки лишаються ті самі («Майстер не працює цього дня» / «поза годинами роботи»).
4. **`PreviewRotation`** — **не чіпати** (прев'ю чистої ротації без БД).

Обмеження: `RotationHelper.WorksOn` **не міняти** (§4.2 AGENTS.md) — override застосовується *поверх* нього.
Форму `POST /api/booking/slots` **не міняти** (§11 AGENTS.md).

**Перевірити на межі доби і на обох типах ротації** (§12 AGENTS.md).

---

## 5. API (адмінка)

Нові ендпоінти в `AdminController` (усі `[Authorize(Roles="Admin")]`):

- `GET /api/admin/masters/{id}/day-overrides` → список override майстра у вікні
  (`[{ date, isWorking, start?, end? }]`). Для заповнення редактора.
  - *Альтернатива:* не робити окремий GET, а віддавати override у складі наявного графіка. Але публічний
    `GET /api/catalog/masters/{id}/schedule` не має ознаки «це override» — для адмін-редактора зручніший
    окремий admin-GET. Узгодити (§9.3).
- `PUT /api/admin/masters/{id}/day-override` — тіло `{ date, isWorking, start?, end? }` → upsert
  (створити або оновити override на дату). Валідація §3.
- `DELETE /api/admin/masters/{id}/day-override?date=YYYY-MM-DD` → прибрати override (повернути дату
  до стандартної поведінки «ротація + заклад»).

Контракт: `camelCase`, enum рядками, помилка `{ "error": "…" }`, успіх без даних `{ "ok": true }`,
`DateOnly`→`"2026-09-10"`, `TimeOnly`→`"09:00:00"` (§4.5 AGENTS.md). Новий параметр DTO — лише останнім.

DTO (позиційний `record` на початку `AdminController.cs`):
`record DayOverrideDto(DateOnly Date, bool IsWorking, TimeOnly? Start, TimeOnly? End);`

---

## 6. Клієнт: клікабельний календар-редактор (Admin.tsx)

**Проблема:** наявний `BookingCalendar` робить `disabled={!available || readOnly}` — клікати можна лише по
«доступних» днях, редактору цього замало (треба клік і по робочих, і по вихідних).

**Рішення (обрано користувачем 2026-09-08): розширити `BookingCalendar` новим режимом.**
Додати режим `mode='edit'` (плюс, за потреби, окремий проп зі станами днів), де:
- комірки **клікабельні незалежно** від `available` (не застосовувати `disabled={!available}` у цьому режимі);
- візуальний стан комірки береться з нового пропа (напр. `dayStates?: Map<string, 'on'|'off'|'override-on'|'override-off'|'override-partial'>`),
  а не з `availableDates`;
- `onSelect(date)` викликається для будь-якого дня у вікні.

Обов'язково **не зламати** наявні режими `mode='schedule'` і `mode='booking'` (використовуються в `/booking`
і `/masters/:id`): нову поведінку вмикати **тільки** під `mode='edit'`, гілки `disabled`/класів розгалузити
за режимом. Перевірити обидва наявні режими після зміни (§12 AGENTS.md).

UX редактора:
- Календар майстра стає **клікабельним** (замість поточного readOnly-прев'ю).
- Візуальні стани дня: робочий-за-ротацією, вихідний-за-ротацією, **override-робочий**, **override-вихідний**,
  **override-частковий (години)** — різні класи (додати в `styles.css`, лише **доповнення**, §6 AGENTS.md).
- Клік по дню → міні-панель/попап із вибором:
  - «Вихідний» (`IsWorking=false`);
  - «Робочий повний день» (`IsWorking=true`, без годин);
  - «Робочий, свої години» (`IsWorking=true` + два `time`-поля Start/End);
  - «Скинути до стандарту» (DELETE override) — якщо на день уже є override.
- Після збереження — перезавантажити графік майстра (як `load()` після інших дій).

`api.ts`: додати обгортки `fetchMasterDayOverrides(id)`, `putMasterDayOverride(id, body)`,
`deleteMasterDayOverride(id, date)`. `types.ts`: `DayOverride` тип.

> Уточнення: чи має редактор бути в **формі редагування майстра** (як зараз прев'ю ротації), чи окремою
> вкладкою/секцією? Пропозиція — там же, де зараз прев'ю ротації (замінити readOnly-прев'ю на редактор,
> коли майстер уже збережений; для нового майстра лишити прев'ю ротації, бо override потребує `MasterId`).
> Узгодити (§9.5).

---

## 7. Міграція

Процедура §7 AGENTS.md:
```
dotnet ef migrations add AddMasterDayOverride --project server
dotnet ef migrations script --project server --output check.sql   # переглянути DDL
dotnet ef database update --project server
dotnet ef migrations list --project server
```
- Нова таблиця `MasterDayOverrides` + unique-індекс `(MasterId, Date)` (у `AppDbContext.OnModelCreating`).
- FK на `Masters` (Cascade при видаленні майстра? Майстри видаляються **м'яко** (`IsActive=false`), тож
  hard-delete рідкісний — Cascade прийнятний). Узгодити (§9.6).
- `check.sql` не лишати в проєкті. Наявні міграції не редагувати. `EnsureCreated`/ручний `ALTER` — заборонено.

---

## 8. Файли під зміну (зведення)

| Файл | Зміна |
|---|---|
| `server/Models/Entities.cs` | + `MasterDayOverride`; (опц.) `Master.DayOverrides` |
| `server/Data/AppDbContext.cs` | `DbSet<MasterDayOverride>`; unique-індекс `(MasterId, Date)` в `OnModelCreating` |
| `server/Migrations/*` | нова міграція `AddMasterDayOverride` |
| `server/Services/ScheduleService.cs` | хелпер `EffectiveDay`; застосувати в `GetSlotsAsync`/`GetMasterScheduleAsync`/`ValidateAsync` (не чіпати `PreviewRotation`, `WorksOn`) |
| `server/Controllers/AdminController.cs` | `record DayOverrideDto`; ендпоінти GET/PUT/DELETE day-override; валідація §3 |
| `client/src/components/BookingCalendar.tsx` | новий режим `mode='edit'` (клікабельні всі дні у вікні, стан із пропа); не ламати `schedule`/`booking` (§6, обрано) |
| `client/src/pages/Admin.tsx` | вбудувати редактор; міні-панель вибору стану дня; перезавантаження після збереження |
| `client/src/api.ts` | `fetchMasterDayOverrides`, `putMasterDayOverride`, `deleteMasterDayOverride` |
| `client/src/types.ts` | тип `DayOverride` |
| `client/src/styles.css` | класи станів днів (лише доповнення) |
| `AGENTS.md` | після реалізації — оновити §9/§11 (нова сутність, ендпоінти) |

---

## 9. Відкриті питання (відповісти перед реалізацією)

1. **Модель (§2):** структура `MasterDayOverride` і семантика 3 станів — ОК? Зворотна навігація
   `Master.DayOverrides` потрібна? Обмежувати дати вікном (сьогодні…+28)?
2. **Валідація годин (§3):** години override **в межах закладу** (пропозиція) чи дозволяти виходити за них?
3. **API (§5):** окремий admin-GET `day-overrides` (пропозиція) чи віддавати у складі графіка?
4. **UI-компонент (§6):** ✅ вирішено — розширити `BookingCalendar` новим режимом `mode='edit'`
   (не окремий компонент). Уточнити лише форму пропа стану днів (`dayStates` Map чи набір Set'ів).
5. **Розміщення редактора (§6):** у формі майстра замість readOnly-прев'ю ротації (пропозиція) чи окрема вкладка?
   Для нового (незбереженого) майстра — лишити прев'ю ротації (override потребує `MasterId`)?
6. **FK-поведінка (§7):** Cascade при (рідкісному) hard-delete майстра — ОК?
7. **Взаємодія з переназначенням при звільненні:** при деактивації майстра його override лишаються «висіти»
   (майстер неактивний → у вибірки не потрапляє). Прибирати їх при деактивації чи лишати? Пропозиція: лишати
   (неактивний майстер і так виключений фільтром `IsActive`). Узгодити.

---

## 10. Обмеження (AGENTS.md — не порушувати)

- `RotationHelper.WorksOn` — єдине джерело ротації (§4.2). Не міняти; override застосовувати поверх.
- `ScheduleService` — єдине джерело правил графіка (§4.4). Уся логіка override — лише там, не в контролерах.
- Форму `POST /api/booking/slots` і `MasterDayDto` (schedule) не міняти за формою (§11, §4.5).
- Транзакція/ізоляція `BookingController.Create` — не чіпати (§4.4).
- Міграції — за процедурою §7; тимчасові `check.sql`/дампи не лишати; застосовані міграції не редагувати.
- `styles.css` — тільки доповнювати (§6). Календар не розширювати новими ендпоінтами понад потрібні (§11 R15
  стосується публічного календаря бронювання; тут — адмінські CRUD-ендпоінти override, що не порушує R15).
```
