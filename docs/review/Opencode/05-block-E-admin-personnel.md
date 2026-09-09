# Блок E — адмінка і персонал (Фази 9, 10, 11)

Джерело: `masters-feature-plan.md` §15–17. Залежність: блок C (`ValidateAsync`, `PreviewRotation`); для фото — блок B; чекбокс `isActive` з E1 потрібен для E2.

## Мета

Керування фото, звільнення з обробкою майбутніх записів, найм без помилок конфігурації.

## Кроки

### E1. Адмінка: фото (Фаза 9, §15)

1. Перед правкою відкрити `Admin.tsx` — перевірити BOM/кирилицю (D6). Якщо мохібейк — перекодувати в UTF-8 і перевірити рендер, лише потім правити.
2. `emptyMaster()` + `photoUrl: ''`; після «Спеціальність» додати `bio`, чекбокс `isActive` («Активний (працює в салоні)»), блок фото тільки для `id > 0` (прев'ю + `input file accept=jpeg/png/webp` + «Прибрати фото»).
3. `onPickPhoto`: клієнтські перевірки типу/3МБ до відправки, `uploadMasterPhoto`, `finally e.target.value=''`. `onRemovePhoto` з `confirm`.
4. Кнопка «Ред.» прокидає `photoUrl: m.photoUrl ?? ''`.
5. `saveMaster`: перевірити що `photoUrl:''` не затирає шлях (рішення §8.4); якщо затирає — не надсилати поле.
6. `styles.css`: `.photo-edit`, `.photo-preview 120×120`, `input[type=checkbox]` inline, `input[type=file]` padding.

### E2. Звільнення (Фаза 10, §16)

1. `NotificationService`: статичний `ResolveRecipient(client, channel)`; використати в `ReminderWorker` + нових місцях.
2. `GET /api/admin/masters/{id}/deactivation-impact`: майбутні `Confirmed/Pending`, сорт за часом, `candidates` через `ValidateAsync` (активні, з послугою, працюють, вільно).
3. `PUT /api/admin/appointments/{id}/master {masterId}`: `ValidateAsync` з `ignoreAppointmentId`, коди 400/404/409, `ReminderSentAt = null`, статус не чіпати, транзакція `Serializable`, нотифікація після `Commit`.
4. `POST /api/admin/masters/{id}/deactivate {policy, reassignments}`: `CancelAll` скасовує з нотифікаціями; `Reassign` вимагає повного покриття інакше 400 без змін; `KeepAppointments` нічого не робить; далі `IsActive=false`, години/послуги/фото лишити. Відповідь `{ok, cancelled, reassigned, kept}`.
5. `DELETE /api/admin/masters/{id}`: soft-гілку прибрати → 409 «Використайте деактивацію...»; hard тільки без записів + видалення фото.
6. `Admin.tsx`: «Видалити» → «Звільнити» з блоком impact (список + select кандидатів + 3 кнопки політики); розділити таблицю на Активні/Неактивні.
7. Логувати всі дії через `ILogger` з `masterId/appointmentId/policy`/JWT-користувачем (AuditLog лишається в роадмапі).

### E3. Найм (Фаза 11, §17)

1. `POST /api/admin/rotation-preview {rotationType, rotationAnchor}` → 15 днів `PreviewRotation` без БД; 400 на невідомий тип.
2. `Admin.tsx`: прев'ю під `rotationAnchor` з debounce 300мс, 15 бейджів, підпис.
3. Валідація `AddMaster/UpdateMaster` 400: без послуг, без годин, `End<=Start`, дубль `DayOfWeek`. Клієнт — `disabled` + підказка.
4. Кнопка «години як у салону 9–19 щодня» (7 днів, бо `emptyMaster` дає Пн–Пт всупереч сіду).

## Перевірка блока (§15.7, §16.11, §17.6)

- E1: 9 пунктів з §15.7 (новий/збережений, 5МБ до відправки нема запиту, exe, прибрати фото, bio/isActive зберігаються, чекбокс не розтягнутий, кирилиця ціла).
- E2: 11 пунктів з §16.11 (impact, CancelAll з логами, Reassign повний/неповний, чужа послуга 400, зайняте 409, зник з головної/schedule, без нагадувань, повернення через isActive, DELETE 409/з фото).
- E3: 8 пунктів з §17.6 (прев'ю 2/2 і тижневе, збіг зі слотами, якір у минулому/майбутньому, 4 валідації, кнопка 7 днів).
- `dotnet build server` без нових warning'ів.

Оцінка: 7–11 год (9:1,5 + 10:3,5–4 + 11:2 за планом, реалістично з запасом).
