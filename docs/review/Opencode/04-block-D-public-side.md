# Блок D — публічна сторона (Фази 5, 6, 7, 8)

Джерело: `masters-feature-plan.md` §11–14. Залежність: блок C. Мінімальний зріз під початковий запит користувача.

## Мета

Картка з фото на головній + окрема сторінка майстра з графіком і бронюванням.

## Кроки

### D1. API сторінки майстра (§11, Фаза 5)

1. `GET /api/catalog/masters/{id}` з явним `[AllowAnonymous]`: поля за §11.1 включно з `photoUrl`, `worksToday/Tomorrow/ThisWeek/NextWeek` через `clock.TodayInSalon`, `services` тільки активні, сорт `Category, Name`. Неактивний → 404.
2. `GET /api/catalog/masters/{id}/schedule` → `schedule.GetMasterScheduleAsync`, рівно 15 елементів, `start/end` `TimeOnly`, `busy` тільки інтервали UTC без даних клієнтів, `Confirmed` + свіжі `Pending`.
3. Типи `MasterDetailDto`, `MasterServiceBriefDto`.

### D2. Типи й обгортки (§12, Фаза 6)

1. `client/src/types.ts`: `MasterDto` + `photoUrl`, опційність `isActive/rotationAnchor/workingHours` (їх дає тільки адмінський ендпоінт); нові `MasterServiceBriefDto`, `MasterDetailDto`, `BusyIntervalDto`, `MasterScheduleDayDto`, `RotationPreviewDayDto`; `MasterForm.photoUrl`.
2. `client/src/api.ts`: `fetchMaster`, `fetchMasterSchedule`, `deleteMasterPhoto`, `previewRotation`, `resolvePhotoUrl` (склейка `API + path`), `uploadMasterPhoto` окремим `fetch` з `FormData` без `Content-Type` (поле `file` як `IFormFile file`).
3. `npm run build` без нових попереджень.

### D3. `MasterCard` (§13, Фаза 7)

1. `client/src/components/` — нова папка; `MasterCard.tsx` з коренем `<Link to={/masters/id}>`, фото `240×240 lazy + alt + onError`, fallback-ініціали `aria-hidden`, текст і бейдж графіка перенести з `Home.tsx` без зміни логіки, CTA «Графік і запис →», контраст `--text` (не `--text-dim` на фото).
2. `Home.tsx`: замінити інлайн-картку на `<MasterCard>`; D7 мінімально — `loading + catch + alive`, повідомлення про помилку замість порожньої сторінки.
3. `styles.css` тільки доповнити стилями картки/фото з §13.3.

### D4. Календар + сторінка `/masters/:id` (§14, Фаза 8)

**Оновлено 2026-09-03 за рішеннями R13–R15: вибір часу став двоетапним — календар з датами, потім слоти обраної дати. Нових ендпоінтів не додавати.**

1. `App.tsx`: роут `/masters/:id` → `MasterSchedule` (публічний, без guards).
2. **`client/src/components/BookingCalendar.tsx`** (новий, §14.4) — спільний для `/booking` і `/masters/:id`:
   - props `availableDates: Set<string>`, `selected`, `onSelect`, `mode: 'schedule' | 'booking'`, `from?`, `windowDays?` (дефолт 14);
   - сітка Пн–Нд, **власний** масив днів тижня (не `DAYS` з `api.ts` — там `0 = Нд`), максимум 3 тижневих рядки, **без навігації по місяцях**;
   - вікно `сьогодні … +14` (15 дат); поза вікном і недоступні — `disabled`, `title` = `Вихідний` / `Немає вільного часу`;
   - `<button type="button">` на комірку, `aria-label` повною датою, `aria-pressed`, `aria-current="date"` на сьогодні, `:focus-visible`;
   - «сьогодні» і будь-яка салонна дата — через `Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Kyiv' })`, **не** `toISOString().slice(0,10)`;
   - запитів не робить.
3. `MasterSchedule.tsx`: `useParams` + `NaN` → «не знайдено»; `Promise.all(fetchMaster, fetchMasterSchedule)` з `loading/err/alive`; шапка з `MasterPhoto` (спільний компонент з D3); далі кроки:
   - **календар робочих днів видно одразу** (`mode="schedule"`, доступність = `isWorking`), під ним робочі години обраної дати і зайняті інтервали `.slot--busy`;
   - крок 1 — послуги кнопками → `fetchSlots(mid, serviceId)`;
   - крок 2 — той самий календар у `mode="booking"`: доступність = `isWorking` **і** є слоти на цю дату;
   - крок 3 — слоти **тільки обраної дати**;
   - крок 4 — `navigate('/booking', {state:{masterId, serviceId, startTime}})`, `startTime` як прийшов з `Z`.
4. `Booking.tsx` (§14.3):
   - групування слотів по **салонній** даті через `sv-SE` + `timeZone`, а не `s.start.slice(0,10)`;
   - крок «Дата» — `BookingCalendar` з `availableDates` = ключі мапи; крок «Час» — слоти лише обраної дати;
   - зміна послуги/режиму скидає `date` і `slot`;
   - prefill: `pendingStart` у `useRef`, після завантаження слотів виставити `date` **і** `slot`; якщо слота вже немає — «Обраний час уже зайнятий, оберіть інший», календар лишити на тій самій даті;
   - банер «Запис до майстра» + «Змінити вибір»; після 409 — перезапит слотів, дату зберегти, слот скинути; прибрати `services?.`.
5. `styles.css` доповнити за §14.5 (блок `/* Календар вибору дати */`: `.calendar`, `.calendar__grid`, `.cal-day` та модифікатори) + mobile 700px з `.calendar { max-width: 100% }`.

## Перевірка блока (§11.3, §13.4, §14.6)

- Без токена обидва API доступні; неактивний → 404; `busy` без `client/phone/service`; 15 елементів; `TwoTwo` чергування 2/2.
- `tsc` чисто.
- Картка: фото/фолбек/битий URL, клік → `/masters/{id}`, Tab+Enter, 320px одна колонка, бек вимкнено → помилка.
- Сторінка майстра: календар робочих днів видно **до** вибору послуги; вихідні `disabled` з `title="Вихідний"`; 16-й день `disabled`; після вибору послуги робочі дати без вільного часу теж `disabled` з `title="Немає вільного часу"`; клік на дату → слоти **лише цієї дати**; вибір → `/booking` на кроці підтвердження.
- `/booking` прямий вхід: послуга → «будь-який майстер» → календар → дата → слоти всіх майстрів → блок «Хто вільний» працює.
- Прихід зі сторінки майстра: календар відкритий на даті слота, слот виділений; якщо слот зайняли — повідомлення, дата збережена.
- Межа доби: о 23:30 за Києвом «сьогодні» в календарі — правильна дата.
- Клавіатура: `Tab` у сітку, `Enter` обирає, фокус видно, `aria-pressed` на обраній.
- 320px: сітка без горизонтального скролу.
- Час збігається з салонним (DST).

Оцінка: 6,5–8,5 год (Фази 5:1 + 6:0,7 + 7:1,5 + 8:4 за планом, плюс запас на D4).
