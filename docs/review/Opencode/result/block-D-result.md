# Результат блока D — публічна сторона (Фази 5–8). Статус: ✅ ВИКОНАНО

Дата: 2026-09-04. План: `docs/review/Kiro/masters-feature-plan.md` §11–14.

## D1. API сторінки майстра (Фаза 5)

- `GET /api/catalog/masters/{id}` — `[AllowAnonymous]`: `MasterDetailDto` (id, name, photoUrl,
  RotationType/Anchor, worksToday/Tomorrow/ThisWeek/NextWeek через `clock.TodayInSalon`,
  services лише активні, сорт Category→Name). Немає id або `!IsActive` → **404** «Майстра не знайдено».
- `GET /api/catalog/masters/{id}/schedule` — `[AllowAnonymous]`, делегує в `schedule.GetMasterScheduleAsync`;
  `KeyNotFoundException` → 404.
- DTO `MasterServiceBriefDto`, `MasterDetailDto` — у `Models/ScheduleDtos.cs`.

## D2. Типи й обгортки (Фаза 6)

- `types.ts`: `MasterDto` + `photoUrl?`, `isActive/rotationAnchor/workingHours` → опційні;
  нові `MasterServiceBriefDto`, `MasterDetailDto`, `BusyIntervalDto`, `MasterScheduleDayDto`,
  `RotationPreviewDayDto`; `MasterForm.photoUrl`.
- `api.ts`: `fetchMaster`, `fetchMasterSchedule`, `resolvePhotoUrl`, плюс до блока E —
  `deleteMasterPhoto`, `uploadMasterPhoto`, `previewRotation`.
- `src/salon.ts` — спільна утиліта салонної дати (`sv-SE` + `Europe/Kyiv`, `salonDate`, `todaySalon`),
  щоб не дублювати `Intl`-логіку в трьох місцях.

## D3. MasterCard (Фаза 7)

- `components/MasterPhoto.tsx` — спільний fallback (ініціали), `loading=lazy`, `alt`, `onError`.
- `components/MasterCard.tsx` — корінь `<Link to={/masters/id}>`, CTA «Графік і запис →», бейдж графіка з Home.
- `Home.tsx`: інлайн-картку → `<MasterCard>`; D7 (loading + `catch` + `alive`), повідомлення про помилку.

## D4. Календар + `/masters/:id` (Фаза 8)

- `components/BookingCalendar.tsx`: сітка Пн–Нд, вікно `сьогодні…+14` (15 дат) без навігації по місяцях,
  `disabled`+`title`, `aria-label/pressed/current`, `:focus-visible`. Запитів не робить.
- `pages/MasterSchedule.tsx`: `useParams`+`NaN`→не знайдено; шапка з фото й типом графіка;
  календар робочих днів одразу (до послуг); крок послуги → календар доступних слотів → слоти обраної дати
  → `navigate('/booking', {state:{masterId, serviceId, startTime}})`.
- `App.tsx`: роут `/masters/:id` (публічний, після `/booking`).
- `Booking.tsx`: групування слотів по салонній даті (`salonDate`, не `slice`); крок «Дата» — календар,
  «Час» — лише обраної дати; скидання date/slot при зміні послуги/режиму; prefill через `pendingStart` +
  повідомлення «Обраний час уже зайнятий»; банер «Запис до майстра» + «Змінити вибір»;
  після 409 — перезапит слотів, слот скидається, дата зберігається; прибрано `services?.`.
- `styles.css` — доповнено блоками §13.3 і §14.5 (картка/фото/сторінка/календар + mobile 700px).

## Адмін-узгодження (мінімальне, з блоку D)

Зміна типів (§12.1) зламала `Admin.tsx` (tst): додано `photoUrl: ''` у `emptyMaster()` і
`isActive: m.isActive ?? true` у форму редагування. Входу блока E (фото/bio/isActive в адмінці) не реалізував —
лише підпорядкування типові. BOM/кирилицю файлу не чіпав.

## Перевірка (що реально запускалось)

- `dotnet build server` → **0 Warning, 0 Error**.
- `tsc -b` → чисто; `vite build` → чисто (46 модулів).
- Smoke (сервер запущено в `Development`):
  - `GET /api/catalog/masters/1` → **200** (JSON: name, photoUrl:null, rotation "TwoTwo", anchor "2026-09-02",
    прапорці, services лише активні 1/2/3).
  - `GET /api/catalog/masters/1/schedule` → **200, рівно 15 елементів**; день 2026-09-04 `isWorking:false`
    (майстер TwoTwo якір 09-02 → 04 законно вихідний).
  - `GET /api/catalog/masters/99999` → **404**.
- Тимчасові smoke-файли прибрано.

## Не перевірено (чесно)

- Ві́зуальний рендер, клавіатуру/A11y, адаптив 320px — без браузера.
- Повний E2E бронювання зі сторінки майстра → `/booking` → код → кабінет.

## Наступний крок

Блок E (Фази 9–11): адмінка (фото/bio/isActive), звільнення, найм. Залежності блока D закрито.