# План: видалення запису адміном (hard-delete) зі синхронізацією скрізь

Актуально на **2026-09-08**. Окрема фіча, **не пов'язана** з планом гостьового бронювання
(`guest-booking-single-code-plan.md`). Стосується вкладки «Бронювання» в адмін-панелі.

> ⚠️ Це **план**, а не реалізація. Нічого не застосовано (тимчасово доданий `DELETE`-ендпоінт
> у `AdminController.cs` було відкочено). Реалізовувати лише після команди «виконуй».

---

## 1. Вимога (від користувача)

Адмін у панелі має могти **видалити запис**, і це видалення повинно:
1. **фізично видалити** запис (hard-delete рядка з таблиці `Appointments`) — рішення користувача, варіант А;
2. так само зникнути **в особистому кабінеті клієнта**;
3. **звільнити час** майстра (запис перестає займати слот).

Пов'язані коди підтвердження (`VerificationCode` цього запису) — **прибирати каскадно** (рішення користувача).

---

## 2. Ключовий факт: єдине джерело правди вже існує

Запис живе в **одній** таблиці `Appointments`. Три місця дивляться на неї, тому синхронізація —
**автоматична** (окремих правок для «зникло в кабінеті» / «звільнило час» не потрібно):

| Місце | Як читає записи | Джерело |
|---|---|---|
| Вільний час майстра | `ScheduleService.ActiveAppointments` бере лише `Confirmed` або свіжий `PendingVerification`; видаленого рядка не існує → час вільний | `server/Services/ScheduleService.cs` |
| Кабінет клієнта | `AccountController.MyAppointments` фільтрує `a.ClientId == userId` по тій самій таблиці → видаленого рядка немає | `server/Controllers/AccountController.cs` |
| Адмін | `AdminController.Appointments` показує всі записи з тієї ж таблиці | `server/Controllers/AdminController.cs` |

**Висновок:** достатньо видалити рядок з `Appointments` — він зникне скрізь сам. Треба лише:
новий серверний `DELETE`-ендпоінт + кнопка в адмін-UI.

---

## 3. Поточний стан (факти з коду)

Серверні ендпоінти для адмін-записів (`AdminController.cs`):

- `GET  /api/admin/appointments` — список.
- `PUT  /api/admin/appointments/{id}/reschedule` — зміна часу.
- `PUT  /api/admin/appointments/{id}/status` — зміна статусу (є `Cancelled`).
- `PUT  /api/admin/appointments/{id}/master` — переназначення майстру.
- **`DELETE` — відсутній.**

Клієнт (`Admin.tsx`):
- стан `appts: AppointmentDto[]`, `load()` тягне `/api/admin/appointments`;
- у рядку таблиці вже є: поле дати/часу (`reschedule`), `<select>` статусу (`setStatus`, `STATUSES`);
- функцій `reschedule(id, startTime)` і `setStatus(id, status)` викликають PUT і роблять `load()`;
- **кнопки «Видалити» немає.**

FK-звʼязок: `VerificationCode.AppointmentId` (nullable int) посилається на `Appointment.Id`.
При hard-delete запису ці коди треба прибрати, інакше — падіння на FK-constraint.

---

## 4. Серверні зміни (детально)

Файл: `server/Controllers/AdminController.cs`

Додати ендпоінт (після `SetStatus`):

```csharp
/// <summary>
/// Повне (hard) видалення запису. Рядок зникає з таблиці Appointments — тому автоматично
/// зникає в кабінеті клієнта (той самий рядок) і звільняє час майстра (ScheduleService
/// більше не бачить запису). Пов'язані коди підтвердження (VerificationCode.AppointmentId)
/// видаляються каскадно вручну, щоб не впасти на FK.
/// </summary>
[HttpDelete("appointments/{id}")]
public async Task<IActionResult> DeleteAppointment(int id, CancellationToken ct)
{
    var appt = await db.Appointments.FindAsync([id], ct);
    if (appt == null) return NotFound(new { error = "Запис не знайдено" });

    var codes = await db.VerificationCodes.Where(v => v.AppointmentId == id).ToListAsync(ct);
    if (codes.Count > 0) db.VerificationCodes.RemoveRange(codes);
    db.Appointments.Remove(appt);
    await db.SaveChangesAsync(ct);
    return Ok(new { ok = true });
}
```

Примітки:
- `AdminController` уже має `[Authorize(Roles="Admin")]` на рівні класу — перевірити й переконатись,
  що новий екшен успадковує авторизацію (не додавати `[AllowAnonymous]`).
- Форма відповіді — `{ ok = true }` (успіх без даних, §4.5 AGENTS.md), помилка — `{ error }`.
- **Без транзакції** достатньо: два `RemoveRange`/`Remove` в одному `SaveChangesAsync` — атомарні.
- Схему БД **не міняємо** — міграція не потрібна (видаляємо дані, не структуру).

---

## 5. Клієнтські зміни (детально)

### 5.1. `client/src/api.ts`

Додати обгортку:
```ts
export const deleteAppointment = (id: number) =>
  api<{ ok: boolean }>(`/api/admin/appointments/${id}`, { method: 'DELETE' })
```

### 5.2. `client/src/pages/Admin.tsx`

- Додати функцію:
  ```ts
  const removeAppointment = async (id: number) => {
    if (!confirm('Видалити запис? Дію не можна скасувати. Запис зникне і в кабінеті клієнта.')) return
    await deleteAppointment(id)
    load()
  }
  ```
- У таблиці бронювань додати **колонку дій** (заголовок, напр. «ДІЇ») і в кожному рядку кнопку:
  ```tsx
  <button className="btn" onClick={() => removeAppointment(a.id)}>Видалити</button>
  ```
- Імпорт `deleteAppointment` з `../api`.
- `load()` після видалення оновить список (зникне рядок).

### 5.3. Стилі

Використати наявний клас `.btn` (за потреби — існуючий модифікатор для небезпечної дії, якщо є).
`styles.css` тільки **доповнювати**, наявні правила не переписувати (§6 AGENTS.md). Новий колір/клас
для «небезпечної» кнопки — **опційно**, узгодити (див. §7).

---

## 6. Порядок реалізації

1. **С1. Сервер**: `DELETE /api/admin/appointments/{id}` (§4).
2. **К1. `api.ts`**: `deleteAppointment` (§5.1).
3. **К2. `Admin.tsx`**: функція + колонка/кнопка «Видалити» + `confirm` (§5.2).
4. **П. Перевірка** (§8).

---

## 7. Відкриті деталі (узгодити перед реалізацією)

1. **Підтвердження видалення.** `window.confirm` (мінімум) — чи окреме модальне вікно? Пропозиція: `confirm`.
2. **Візуал кнопки.** Звичайна `.btn` — чи окремий «небезпечний» стиль (червоний)? Пропозиція: звичайна `.btn`,
   без нового CSS (мінімальна зміна); якщо треба акцент — доповнити `styles.css` окремим класом.
3. **Місце кнопки.** Нова колонка «Дії» в кінці таблиці — чи кнопка поряд зі статусом? Пропозиція: нова колонка.
4. **Чи лишати `Cancelled` у статусах.** Видалення ≠ скасування. Пропозиція: лишити `<select>` статусу як є
   (скасування — окрема м'яка дія), а видалення — додаткова кнопка. Не змішувати.

---

## 8. Перевірка (перед «готово», §12 AGENTS.md)

1. `dotnet build server` — без нових warning'ів.
2. `cd client; npm run build` — tsc + vite без помилок.
3. Видалити запис в адмінці → рядок зникає зі списку адміна.
4. Той самий запис зник у кабінеті клієнта (`/cabinet`, `AccountController.MyAppointments`).
5. Час майстра звільнився: слот, який займав видалений запис, знову доступний у
   `POST /api/booking/slots` і `GET /api/catalog/masters/{id}/schedule`.
6. У БД: рядок `Appointments` відсутній; пов'язані `VerificationCode` (за `AppointmentId`) теж видалені;
   FK-помилок немає.
7. Видалення неіснуючого `id` → `404 { error }`.

---

## 9. Обмеження / застереження

- **Незворотність.** Hard-delete не можна відкотити; запис зникає з історії клієнта повністю.
  Це свідомий вибір користувача (варіант А), на відміну від м'якої деактивації майстрів/послуг (AGENTS.md §4.3).
- Авторизація: ендпоінт лише для `Admin` (успадковує `[Authorize(Roles="Admin")]` класу) — не послаблювати.
- Схема БД не змінюється; міграція не потрібна.
```
