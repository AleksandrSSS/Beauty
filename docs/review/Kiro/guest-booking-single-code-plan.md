# План: бронювання без входу + єдиний код + автологін у кабінет

Актуально на **2026-09-08**. Замінює вимоги §3.9/§3.10 документа `master-schedule-booking-fix.md`
(варіант A з двома кодами) на нову модель: **гостьове бронювання за телефоном з одним кодом**.

> ⚠️ Це **план**, а не реалізація. Нічого з наведеного ще не застосовано.
> Порядок реалізації — суворо за §8. Перед стартом — прочитати §2 (рішення) і §7 (правила, що переступаємо).

---

## 1. Мета (з вимог користувача)

1. **Створити запис можна без залогіненого користувача.** Достатньо актуального номера телефону
   і обраного каналу надсилання коду.
2. **Один код.** Натиснув «Отримати код» → ввів код → запис зафіксовано в БД (`Confirmed`) →
   користувача **автоматично залогінено** і перекинуто в його кабінет (`/cabinet`).
3. **Реєстрація/вхід у кабінет — лише за номером телефону** (без пароля для клієнта; пароль лишається
   тільки для адміна).
4. **Прив'язка запису — за телефоном.** Клієнт ідентифікується телефоном.

---

## 2. Ключове архітектурне рішення (підтверджено користувачем)

### 2.1. `ClientId` лишається `int` FK на `User.Id`

Технічний факт із коду (`Models/Entities.cs`): `Appointment.ClientId` — це `int`, зовнішній ключ на
`User.Id`. Телефон зберігається окремо в `User.Phone` (рядок). Зробити сам `ClientId` рядком-телефоном
неможливо без ламання всієї схеми (FK, JWT-клейм `NameIdentifier`, кабінет, адмінка).

**Рішення:** «прив'язка за телефоном» реалізується через **find-or-create `User` за телефоном**:
за введеним телефоном знаходимо наявного `User` або створюємо нового, і його `Id` іде в `Appointment.ClientId`.
Телефон лишається природним ідентифікатором клієнта; `ClientId (int)` — внутрішній технічний зв'язок,
користувач його не бачить. Це виконує вимогу «звʼязування за телефоном» без зміни типу ключа.

### 2.2. Один код замість двох

Зараз існує **два окремі коди**: код входу (`auth/request-code` → `auth/verify-code`, `Purpose="login"`)
і код підтвердження запису (`booking/create` → `booking/confirm`, `Purpose="booking"`).

**Рішення:** для гостьового бронювання використовується **тільки код підтвердження запису**
(`Purpose="booking"`). Окремий вхід не потрібен: `booking/confirm` після успіху **сам видає JWT** —
підтвердження запису одночасно є входом. Один код, один крок.

### 2.3. Найбільший фактичний факт: шаблон уже є

`AuthController.VerifyCode` **вже реалізує** find-or-create `User` за телефоном і видачу JWT
(`jwt.CreateToken(user)` + повернення `user`-обʼєкта). Новий `booking/confirm` повторює цей самий підхід —
це знижує ризик і обсяг роботи.

---

## 3. Поточний стан (факти з коду — база для правок)

| Місце | Поточна поведінка | Джерело |
|---|---|---|
| `BookingController.Create` | `[Authorize]`; `userId = JwtService.GetUserId(User)`; телефон з JWT-клейму `MobilePhone` (`!.Value`); `ClientId = userId`; шле код `Purpose="booking"` | `Controllers/BookingController.cs` |
| `BookingController.Confirm` | `[Authorize]`; шукає запис за `AppointmentId` **і** `ClientId == userId`; переводить у `Confirmed`; **не видає JWT** | те саме |
| `CreateBookingDto` | `(int MasterId, int ServiceId, DateTime StartTime, NotifyChannel Channel, string? Contact)` — **немає телефону/імені** | те саме |
| `AuthController.VerifyCode` | `[AllowAnonymous]`; find-or-create `User` за телефоном; видає `{ token, user }` | `Controllers/AuthController.cs` |
| `Program.cs` | Global auth policy **немає**; публічність тримається на відсутності `[Authorize]` на конкретному екшені | `Program.cs` |
| `Booking.tsx` | Для `user==null` показує **міні-форму входу** (`request-code`/`verify-code`), після входу — окремий крок коду підтвердження запису (**два коди**) | `client/src/pages/Booking.tsx` |
| `createBooking` (клієнт) | body: `{ masterId, serviceId, startTime, channel, contact }` — **без телефону** | `client/src/api.ts` |
| `confirmBooking` (клієнт) | body: `{ appointmentId, code }` → повертає `{ ok }` | те саме |
| Кабінет | `AccountController.MyAppointments` фільтрує `a.ClientId == userId` → після автологіну запис видно, бо `ClientId` = той самий `User` | `Controllers/AccountController.cs` |
| Профіль/канал | `AccountController.UpdateProfile` приймає `PreferredChannel`/`ExternalContact`; `Cabinet.tsx` їх редагує й зберігає → **канал інформування вже змінюється в кабінеті** | `Controllers/AccountController.cs`, `client/src/pages/Cabinet.tsx` |

**Висновок:** серверна логіка **мусить змінитись** — вимога «створити запис без входу» технічно
несумісна з поточними `[Authorize]` + читанням телефону з JWT.

---

## 4. Цільовий потік (після правок)

### 4.1. Гостьове бронювання (незалогінений)

```
Крок «Підтвердження» на /booking (або зі сторінки майстра):
  1. Поле «Телефон» (+380, нормалізація) + вибір каналу коду + опційний контакт
  2. Кнопка «Отримати код»
       → POST /api/booking/create  { masterId, serviceId, startTime, channel, contact, phone, name? }
       → сервер: find-or-create User(phone); Appointment(PendingVerification, ClientId=user.Id);
                 код Purpose="booking"; надсилає код обраним каналом
       → відповідь: { appointmentId, mock, info, devCode? }   (форма НЕ міняється)
  3. Поле «Код підтвердження запису»
  4. Кнопка «Підтвердити запис»
       → POST /api/booking/confirm  { appointmentId, code, phone }
       → сервер: перевіряє код за appointmentId+phone; Status=Confirmed;
                 видає JWT для User(phone)
       → відповідь: { ok, token, user }        (форма РОЗШИРЮЄТЬСЯ: +token +user)
  5. Клієнт: login(token, user) → nav('/cabinet')   ← автологін, кабінет
```

### 4.2. Залогінений користувач — **без коду, без вибору каналу/контакту**

Залогінений уже підтвердив телефон при вході і має в профілі (`User`) телефон, `PreferredChannel`
і `ExternalContact`. Тому на кроці «Підтвердження» він **нічого не вводить** — ні коду, ні каналу,
ні контакту. Все береться з `User` на сервері.

```
Крок «Підтвердження» (user != null):
  1. Показ: послуга · майстер · час  (без полів вводу)
  2. Кнопка «Забронювати»
       → POST /api/booking/create  { masterId, serviceId, startTime }
       → сервер (автентифікований): бере телефон і канал з User;
                 Appointment одразу Status=Confirmed; коду НЕ шле;
                 надсилає повідомлення «Запис підтверджено…» на дані з профілю
       → відповідь: { appointmentId, confirmed: true, mock, info, devCode: null }
  3. Клієнт: nav('/cabinet')   ← лишаємось залогіненими
```

Вибір каналу/контакту і код (`PendingVerification` + `confirm`) — **тільки для гостя** (§4.1),
бо гостю треба вказати, куди слати код, і підтвердити володіння телефоном.

> **Канал інформування залогінений змінює в особистому кабінеті** (не на кроці бронювання).
> Це вже реалізовано: `AccountController.UpdateProfile` приймає `PreferredChannel`/`ExternalContact`,
> а `Cabinet.tsx` їх редагує й зберігає. Тобто джерело правди каналу для залогіненого — профіль `User`,
> і `create` бере його звідти (§6.2). Нічого нового для цього додавати не треба — лише **не дублювати**
> вибір каналу на кроці бронювання для залогіненого.

---

## 5. Відкриті деталі реалізації (узгодити перед §8, але не блокують написання плану)

1. **Залогінений і код — ВИРІШЕНО (варіант 2, без коду).** Для вже залогіненого користувача коду
   при бронюванні **немає**: `create` одразу створює запис `Confirmed` і не шле код. Код (`PendingVerification`
   → `confirm`) лишається **тільки для гостя**. Це вимагає розгалуження в `create` (див. §6.2): гілка
   «автентифікований → одразу Confirmed» vs «гість → PendingVerification + код». Відповідь `create` містить
   ознаку `confirmed` (bool), щоб клієнт знав, показувати крок коду чи одразу вести в кабінет.
2. **`phone` у `confirm`.** Передавати телефон у `confirm` (гість не має JWT) — так; для залогіненого
   сервер бере телефон з `User`. `phone` у DTO `confirm` — **новий параметр останнім** (§5 AGENTS.md).
3. **Ім'я нового клієнта.** Гість може не ввести ім'я. Дефолт — `"Клієнт"` (як у `VerifyCode`). Поле «Ім'я»
   опційне в кроці підтвердження. Пропозиція: показувати необов'язкове поле «Ім'я».
4. **UNIQUE-індекс на `User.Phone`.** Підтверджено «так». Потрібна міграція (§6.4). Ризик: якщо в БД
   вже є дублі телефонів — міграція впаде; спершу перевірити (`SELECT phone, count(*) ... group by ... having count(*)>1`).
5. **`create` для гостя** повертає `confirmed=false` (потрібен крок коду); для залогіненого `confirmed=true`
   (одразу в кабінет, без коду). `appointmentId` лишається в обох випадках.
6. **`confirm` викликається лише для гостя** (у залогіненого запис уже `Confirmed`). Для гостя `confirm`
   і видає JWT (автологін).
7. **Rate-limiting гостьового `create`** — легкий per-phone ліміт **у межах цього плану** (§6.6):
   макс. 3 коди `Purpose="booking"` на телефон за 15 хв, інакше `429`. Без нового поля/міграції
   (проксі-час через `ExpiresAt`). Повний per-IP throttle лишається окремою задачею P0-2 (§11).
8. **Канал інформування для залогіненого — з кабінету, не з бронювання.** `User.PreferredChannel`/
   `ExternalContact` вже редагуються в кабінеті (`AccountController.UpdateProfile` + `Cabinet.tsx`) — див. факт
   у §3. Тому на кроці бронювання залогінений канал **не обирає**; `create` бере його з `User` (§6.2).
   Гостю ж треба обрати канал на місці (він ще не має профілю). **Серверних/клієнтських правок для самої
   зміни каналу не потрібно** — функціонал у кабінеті вже є; план лише спирається на нього.

---

## 6. Серверні зміни (детально)

### 6.1. `CreateBookingDto` — додати телефон та ім'я (останніми параметрами)

Файл: `server/Controllers/BookingController.cs`

```
// БУЛО:
public record CreateBookingDto(int MasterId, int ServiceId, DateTime StartTime, NotifyChannel Channel, string? Contact);

// СТАЄ (нові параметри — лише в кінці, §5 AGENTS.md):
public record CreateBookingDto(int MasterId, int ServiceId, DateTime StartTime, NotifyChannel Channel, string? Contact, string Phone, string? Name);
```

Клієнт слатиме різні набори полів залежно від гілки:
- **гість** — `phone`, `name?`, `channel`, `contact?`, `startTime`, `masterId`, `serviceId`;
- **залогінений** — лише `masterId`, `serviceId`, `startTime` (решта ігнорується сервером, бо береться з `User`).

Рішення: для залогіненого сервер **ігнорує** `dto.Phone`/`dto.Name`/`dto.Channel`/`dto.Contact` і бере
телефон та канал з `User`; для гостя — використовує значення з DTO.

### 6.2. `BookingController.Create` — `[AllowAnonymous]` + дві гілки (гість vs залогінений)

Файл: `server/Controllers/BookingController.cs`

- Змінити атрибут: `[Authorize]` → `[AllowAnonymous]`.
- Визначити телефон, клієнта і **гілку поведінки**:
  - **Залогінений** (`User.Identity?.IsAuthenticated == true`): `userId = GetUserId(User)`,
    телефон **і канал** з наявного `User` (завантажити з БД за `userId`; канал — `User.PreferredChannel`,
    контакт — `User.ExternalContact`). Поля `dto.Channel`/`dto.Contact`/`dto.Phone`/`dto.Name` **ігноруються**.
    Запис створюється одразу зі `Status = Confirmed`, **код НЕ генерується і НЕ шлеться**. Надіслати
    повідомлення «Запис підтверджено…» (як у поточному `Confirm`) на дані з профілю.
  - **Гість** (не автентифікований): `phone = dto.Phone.Trim()`; валідація `phone.Length >= 9`
    (як у `RequestCode`), інакше `400`; find-or-create `User` за телефоном (підхід із
    `AuthController.VerifyCode`: якщо нема — створити з `Name = dto.Name ?? "Клієнт"`,
    `PreferredChannel = dto.Channel.ToString()`, `ExternalContact = dto.Contact`); `userId = user.Id`.
    Запис `Status = PendingVerification`; згенерувати код `Purpose="booking"` і надіслати обраним каналом.
- `Appointment.ClientId = userId` (як і зараз).
- Для гостя `VerificationCode.Phone` = визначений телефон (не з JWT-клейму!).
  **Прибрати** читання `User.FindFirst(ClaimTypes.MobilePhone)!.Value` — воно кине NRE для гостя.
- Транзакція `Serializable` (§4.4 AGENTS.md) — **зберегти** в обох гілках. Нотифікація — **після**
  `CommitAsync()` (зберегти).
- **Форма відповіді розширюється ознакою `confirmed`** (щоб клієнт знав, чи потрібен крок коду):
  ```
  { appointmentId, confirmed: bool, mock, info, devCode? }
  ```
  - залогінений: `confirmed = true`, `devCode = null`;
  - гість: `confirmed = false`, `devCode` як зараз (DEV + mock).

> Увага (§4.4/§4.6 AGENTS.md): не знижувати рівень ізоляції; `[AllowAnonymous]` ставимо **явно**.
> Додавання `confirmed` — сумісне розширення (наявні поля лишаються), але клієнтський тип оновити (§7.2).

### 6.3. `BookingController.Confirm` — `[AllowAnonymous]` + пошук за телефоном + видача JWT (лише гість)

Файл: `server/Controllers/BookingController.cs`

`Confirm` тепер обслуговує **тільки гостьовий шлях** (у залогіненого запис уже `Confirmed` через `create`,
§6.2). Але залишаємо метод стійким і на випадок автентифікованого виклику.

- `ConfirmBookingDto`: додати `Phone` останнім → `(int AppointmentId, string Code, string? Phone)`.
- Атрибут: `[Authorize]` → `[AllowAnonymous]`.
- Визначити телефон/користувача:
  - Гість (не автентифікований): телефон з `dto.Phone`; знайти `User` за телефоном; шукати запис за
    `AppointmentId && ClientId == user.Id`.
  - Автентифікований (запасний шлях): `userId = GetUserId(User)`, шукати за `AppointmentId && ClientId == userId`.
- Перевірка коду — як зараз (`VerificationCodes` за `AppointmentId`, `Code`, не consumed, не прострочений).
- На успіху: `Status = Confirmed`, `vc.ConsumedAt = now`, `SaveChangesAsync`, надіслати повідомлення.
- **Нове:** згенерувати JWT для `User` (`jwt.CreateToken(user)`) і повернути
  `{ ok = true, token, user = { id, phone, name, email, role, preferredChannel } }`
  (обʼєкт `user` — як у `VerifyCode`, щоб клієнтський `UserInfo` збігся).
- DI: додати `JwtService jwt` у primary-конструктор `BookingController`.

> Форма відповіді `confirm` **розширюється** (додаються `token`, `user`). Це зміна контракту —
> оновити `client/src/api.ts` і `types.ts` (§6.5/§7).

### 6.4. Міграція: UNIQUE-індекс на `User.Phone`

Файл: `server/Data/AppDbContext.cs` (`OnModelCreating`) + нова міграція.

- Перед міграцією — **перевірити дублі** в живій БД:
  `SELECT "Phone", count(*) FROM "Users" GROUP BY "Phone" HAVING count(*) > 1;`
  Якщо є — вирішити вручну (об'єднати/видалити), інакше `CREATE UNIQUE INDEX` впаде.
- Додати в `OnModelCreating`: `modelBuilder.Entity<User>().HasIndex(u => u.Phone).IsUnique();`
- Команди (§7 AGENTS.md):
  ```powershell
  dotnet ef migrations add AddUserPhoneUniqueIndex --project server
  dotnet ef migrations script --project server --output check.sql   # переглянути DDL, потім видалити
  dotnet ef database update --project server
  dotnet ef migrations list --project server
  ```
- **Заборони (§7 AGENTS.md):** не редагувати застосовані міграції, не `EnsureCreated`, не `down -v`,
  не відкат `database update 0`. Тимчасовий `check.sql` не лишати в проєкті.

> ⚠️ Це єдина зміна схеми в плані. Якщо перевірка дублів показує ризик — узгодити з користувачем до `database update`.

### 6.5. `Program.cs`

Перевірити: global auth policy немає (факт §3) → `[AllowAnonymous]` на `create`/`confirm` достатньо,
додаткових змін у pipeline не потрібно. **Нічого не міняти**, лише переконатись після правок.

### 6.6. Легкий rate-limit гостьового `create` (без нового поля, без міграції)

Файл: `server/Controllers/BookingController.cs` (гілка гостя в `Create`).

**Мета:** не дати анонімно спамити кодами/сміттєвими записами на один телефон. Легкий захист у межах цього
плану (повноцінний middleware-rate-limiting лишається окремою задачею P0-2 — див. §11).

**Рішення — ліміт «скільки кодів `Purpose="booking"` видано на телефон за останні N хвилин».** Використовуємо
наявне поле `ExpiresAt` як проксі часу створення (код живе рівно 10 хв, `ExpiresAt = UtcNow.AddMinutes(10)`),
тому **нове поле й міграція не потрібні**:

- Параметри (в код або `appsettings.json` секція `Booking:GuestRateLimit`): `WindowMinutes = 15`, `MaxCodes = 3`.
- Перед створенням коду для гостя порахувати:
  ```
  свіжі коди = VerificationCodes.Count(v =>
      v.Phone == phone && v.Purpose == "booking"
      && v.ExpiresAt > DateTime.UtcNow.AddMinutes(-(WindowMinutes - 10)));
  // ExpiresAt > now-(W-10)  ⇔  створено пізніше, ніж (now - W)
  ```
  > Примітка: оскільки `ExpiresAt = created + 10хв`, умова «створено за останні W хв» = `ExpiresAt > now-(W-10)`.
  > При `W=15` це `ExpiresAt > now-5хв`. Якщо в майбутньому час життя коду зміниться — переглянути формулу
  > (або додати справжнє `CreatedAt` окремою міграцією; поза цим планом).
- Якщо `свіжі коди >= MaxCodes` → повернути `429 Too Many Requests` з
  `{ error = "Забагато запитів коду. Спробуйте за кілька хвилин." }` (українською, §4.5 AGENTS.md).
- Робити перевірку **до** створення `Appointment`/`VerificationCode` і поза межами / на початку транзакції,
  щоб не плодити сміттєві `PendingVerification` записи.
- Ліміт стосується **лише гостьової гілки**; залогінений код не отримує (§4.2), тож його це не блокує.

**Обмеження цього підходу (чесно):**
- Ліміт per-phone, не per-IP — зловмисник може перебирати різні телефони. Повний захист (per-IP,
  глобальний throttle) — задача P0-2, свідомо поза планом.
- Проксі-час через `ExpiresAt` крихкий до зміни TTL коду. Прийнятно для тестового проєкту; задокументовано.

---

## 7. Клієнтські зміни (детально)

### 7.1. `client/src/api.ts`

- `createBooking`: додати `phone` і `name` в body-тип (для залогіненого можна слати без них — сервер ігнорує):
  ```
  { masterId, serviceId, startTime, channel, contact, phone, name }
  ```
- `confirmBooking`: додати `phone` у body; тип відповіді — `{ ok: boolean; token: string; user: UserInfo }`.

### 7.2. `client/src/types.ts`

- `BookingCreateResponse`: додати `confirmed: boolean` (гість `false`, залогінений `true`).
- Додати тип відповіді `confirm`:
  ```
  export interface BookingConfirmResponse { ok: boolean; token: string; user: UserInfo }
  ```

### 7.3. `client/src/pages/Booking.tsx`

- **Прибрати міні-форму входу** (`request-code`/`verify-code`, стани `loginPhone/loginChannel/loginContact/
  loginName/loginCode/loginSent/loginInfo/loginErr`, функції `sendLoginCode`, `verifyAndBook`).
  Причина: окремий вхід більше не потрібен (§2.2).
- Крок «Підтвердження» для **гостя** (`user == null`):
  - поле «Телефон» (`normalizePhone`, дефолт `+380`), вибір каналу (`CHANNELS`), опційний контакт, опційне ім'я;
  - кнопка «Отримати код» → `createBooking({ ..., phone, name })` → `setPending(r)` + показ `msg`/`devCode`;
  - після `pending` (і `confirmed === false`) — поле «Код підтвердження запису» + кнопка «Підтвердити запис» →
    `confirmBooking({ appointmentId, code, phone })` → `login(r.token, r.user)` → `nav('/cabinet')`.
- Крок «Підтвердження» для **залогіненого** (`user != null`) — **без коду, без полів вводу**:
  - лише показ «послуга · майстер · час» + кнопка «Забронювати»;
  - `create({ masterId, serviceId, startTime })` (канал/контакт/телефон сервер бере з `User`);
  - відповідь `confirmed === true` → **одразу** `nav('/cabinet')`, крок коду й вибір каналу **не показувати**.
- `create()`/`doCreate()`: прибрати ранній `if (!user) return`/редирект; для гостя додати `phone`/`name` в body.
  Після відповіді розгалужувати за `r.confirmed`: `true` → `nav('/cabinet')`; `false` → показати поле коду.
- `confirm()`: лише гостьовий шлях — після успіху `login(r.token, r.user)` + `nav('/cabinet')`.
- Нумерація/структура кроків «Підтвердження» — зберегти єдиний блок.

### 7.4. Телефон у полі (§3.8 попереднього документа)

`normalizePhone` у `salon.ts` **вже реалізовано** і застосовано в `Login.tsx`. У новому полі телефону
на кроці «Підтвердження» використати той самий `normalizePhone`. Нічого в `salon.ts` не міняти.

### 7.5. Стилі

Використати наявні класи (`.step`, `.slot`, `.btn`, `.link`, `.error`, `.muted`, `.login-inline` якщо
лишиться доречним). `styles.css` тільки **доповнювати** за потреби, наявні правила не переписувати (§6 AGENTS.md).

---

## 8. Порядок реалізації (суворий)

Сервер спершу (контракт), далі клієнт, наприкінці перевірка. Міграцію робити **до** правок логіки `create`/`confirm`
не обов'язково — вона незалежна, але UNIQUE-індекс краще додати рано, щоб виявити дублі.

1. **С0. Перевірка дублів телефонів** у БД (§6.4) — до міграції.
2. **С1. Міграція** `AddUserPhoneUniqueIndex` (§6.4).
3. **С2. Сервер `create`** (§6.1, §6.2): DTO + `[AllowAnonymous]` + find-or-create + телефон не з JWT.
4. **С3. Сервер `confirm`** (§6.3): DTO + `[AllowAnonymous]` + пошук за телефоном + видача JWT + DI `JwtService`.
5. **К1. `api.ts` + `types.ts`** (§7.1, §7.2): нові поля body й тип відповіді `confirm`.
6. **К2. `Booking.tsx`** (§7.3): прибрати міні-форму входу; гість — телефон+код; автологін+редирект.
7. **П. Перевірка** (§9).
8. **Д. Оновити документацію** (§10): AGENTS.md §11 і §9, попередній документ §3.9/§3.10.

---

## 9. Перевірка (перед «готово», за §12 AGENTS.md)

1. `dotnet build server` — без нових warning'ів.
2. `cd client; npm run build` — tsc + vite без помилок.
3. `dotnet ef migrations list` — `AddUserPhoneUniqueIndex` присутня; у psql — унікальний індекс на `Users.Phone`.
4. `POST /api/auth/admin-login` → 200 (не зачеплено, але торкались auth-зони).
5. Сценарій **гість**: незалогінений проходить бронювання → отримує код (DEV: `devCode`) → підтверджує →
   опиняється в `/cabinet` залогіненим, запис видно (`Confirmed`), у БД `Appointment.ClientId` = `User` за телефоном.
6. Сценарій **повторний гість тим самим телефоном**: другий запис в'яжеться до **того самого** `User`
   (find, не create) — перевірити, що дубля користувача немає (UNIQUE тримає).
7. Сценарій **залогінений**: бронює → **без коду** запис одразу `Confirmed` → лишається залогіненим →
   `/cabinet`; крок вводу коду відсутній, `create` повернув `confirmed=true`.
8. `POST /api/booking/slots` — форма відповіді не змінилась (обидва режими `masterId`/`null`).
9. Час на межі доби (23:30 Києва) — дата слота коректна (не зачіпали `SalonClock`/`salonDate`, лише переконатись).
10. Стверджувати «працює» лише про реально запущене.

---

## 10. Документи під оновлення (після реалізації)

| Файл | Що оновити |
|---|---|
| `AGENTS.md` §11 | зняти заборону «серверну частину не додавати» для цього кейсу; зафіксувати, що `booking/create` і `booking/confirm` тепер `[AllowAnonymous]`, приймають телефон у тілі й роблять find-or-create клієнта за телефоном; `confirm` видає JWT (автологін) |
| `AGENTS.md` §9 (відомі дефекти) | оновити рядок про `BookingController` (телефон більше не з `MobilePhone`-клейму → NRE-ризик усунено для гостя) |
| `AGENTS.md` §4.6 | зафіксувати виняток: `create`/`confirm` свідомо анонімні |
| `docs/review/Kiro/master-schedule-booking-fix.md` §3.9/§3.10/§5.7 | позначити застарілим: варіант A (два коди) замінено на гостьове бронювання з одним кодом (посилання на цей документ) |

---

## 11. Ризики та застереження

- **Rate-limiting відсутній** для анонімного `create` — потенційний спам кодами/створення записів.
  Поза планом (P0-2). Зафіксовано як відомий ризик.
- **Дублі телефонів** у БД до міграції зламають UNIQUE-індекс — обов'язкова перевірка С0.
- **Зміна контракту `confirm`** (+`token`,+`user`) — клієнт і сервер оновлюються разом; стара збірка клієнта
  з новим сервером ігноруватиме `token` (не критично), новий клієнт зі старим сервером — `login()` впаде
  (немає токена) → деплоїти разом.
- **Гість без імені** → `Name = "Клієнт"`; користувач може перейменуватись у профілі (поза планом).
- **Ізоляція транзакції** `Serializable` у `create` — **не знижувати** (§4.4).
```
