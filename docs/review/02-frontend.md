# 02 — Frontend: детальний розбір

Стек: React 18 + Vite + TS + react-router. Структура `client/src`: `api.ts, App.tsx, types.ts, styles.css, pages/{Home,Booking,Login,Cabinet,Admin}.tsx`.

## 2.1. Конфіг і збірка

**F-Critical-1. `.env` розсинхрон.**
`.env`: `VITE_API_URL=` (пусто). `.env.example`: `http://localhost:5000` + зайвий `VITE_AUTH_URL=http://localhost:3001` (такого сервісу нема). `api.ts:13` `import.meta.env.VITE_API_URL || ''` → prod-білд ходить відносним `/api/...`, dev — через proxy. Розбіжність dev/prod, відсутня валідація env. Фікс: `VITE_API_URL=http://localhost:5000` в dev, в prod — явний домен; прибрати `VITE_AUTH_URL`; додати `zod`-перевірку env на старті.

**F-Major-2. `vite.config.ts:6`** — proxy як рядок, без `changeOrigin`, без `build.target/sourcemap`, без `test`. Додати об'єктний proxy + `server.port 5173 strictPort`.

**F-Major-3. `tsconfig.json:15-18`** — `strict:true` добре, але `noUnusedLocals:false, noUnusedParameters:false` приховують мертвий код. Нема `noUncheckedIndexedAccess`, `vite.config.ts` не входить в `include:["src"]` і не перевіряється `tsc -b`.

**F-Major-4. `package.json:6-10`** — тільки `react/router/vite/ts`. `build: tsc -b && vite build` падає на першій тип-помилці. Додати `lint, test (vitest), format`.

## 2.2. TypeScript і структура

* Плоска структура, нема `components/hooks/context/router/utils/ErrorBoundary/Layout/ProtectedRoute`. При 5 сторінках ще ок, далі не масштабується.
* Мертвий код, який приховує tsconfig: `Booking.tsx:3` `import {api}` не використовується; `api.ts:70` `DAYS` ніде не імпортується.
* Втрата типобезпеки:
  - `Booking.tsx:14` `useState('Sms')` — `string`, а не `ChannelId`; `api.ts:57` `createBooking {channel:string}`.
  - `Admin.tsx:6,80` `STATUSES:string[], setStatus(id,status:string)` замість `AppointmentStatus`.
  - `Booking.tsx:65` `m.services?.includes` — в `MasterDto` поле `services:number[]` required, `?.` маскує розбіжність контракту з беком (бек повертає `services` як `IEnumerable`, фронт чекає масив — при пустому буде `undefined`).

## 2.3. React: стан, роутинг, форми — критичне

**F-Critical-10. Нереактивний auth — `api.ts:15-16`, `App.tsx:2,20-28`.**
```ts
export let user: UserInfo | null = null // мутабельний синглтон
// App.tsx
import { user as currentUser } from './api'
{currentUser ? <NavLink to="/cabinet"> : <NavLink to="/login">}
```
`App` не підписаний на зміни. Після `setAuth()` хедер не перерендериться — «Кабінет/Адмін/Вийти» не з'являться до reload. Те саме `Cabinet.tsx:14,20`, `Admin.tsx:34,36`, `Booking.tsx:45` читають `user` з модуля в момент рендеру. Фікс: `AuthContext + useSyncExternalStore` або Zustand:
```tsx
const AuthCtx = createContext<{user:UserInfo|null, setAuth...}>(...)
```

**F-Critical-11. Нема Route Guards — `App.tsx:32-38`.**
```tsx
<Route path="/cabinet" element={<Cabinet />} />
<Route path="/admin" element={<Admin />} />
```
Будь-хто відкриває URL напряму, перевірка лише текстом «Увійдіть...» / «Доступ лише...». Потрібно:
```tsx
<Route path="/cabinet" element={<ProtectedRoute><Cabinet/></ProtectedRoute>} />
<Route path="/admin" element={<ProtectedRoute role="Admin"><Admin/></ProtectedRoute>} />
<Route path="*" element={<NotFound/>} />
```

**F-Critical-12. Баг селектів — `Booking.tsx:73,80`.**
```tsx
<select value={serviceId ?? ''} onChange={e => setServiceId(+e.target.value)}>
```
`+'' === 0` — вибір «— оберіть —» дає `0`, а не `null`. `disabled={!serviceId}` ламається (0 — falsy, але `masterId=0` піде в `fetchSlots(0,...)`). Фікс: `e.target.value === '' ? null : Number(...)`.

**F-Major-13. Race без Abort — `Booking.tsx:26-31`, `Home.tsx:10-13`.**
```tsx
useEffect(() => { if (masterId && serviceId) fetchSlots(...).then(setSlots) }, [masterId,serviceId])
```
Швидка зміна майстра: старий запит приходить пізніше і перезаписує свіжі слоти. Нема `loading/catch`, `setState` після unmount. Фікс: `AbortController + signal` в `api()`, `try/catch`, `Promise.all` для `fetchServices+fetchMasters`.

**F-Major-14. Форми без валідації — `Login.tsx:50,66`, `Booking.tsx:122,132`, `Admin.tsx:107-108`.**
Телефон/код/ім'я/ціна/тривалість — нема `required/pattern/maxLength/inputMode`, нема `disabled` під час запиту → дабл-сабміт. `+e.target.value` дає `NaN` при очищенні ціни. Потрібні контрольовані форми + `disabled={loading}` + `react-hook-form/zod` при рості.

## 2.4. API-клієнт

**F-Critical-16. Нема 401/refresh — `api.ts:26-38`.**
```ts
if (!res.ok) throw new Error(data.error || `Помилка ${res.status}`)
```
Протухлий JWT (24г) — вічна «Помилка», без `logout/redirect`. Refresh-логіки нема взагалі. Додати:
```ts
if (res.status === 401) { setAuth(null,null); location.href='/login'; throw ... }
```

**F-Critical-17. Токен в localStorage — `api.ts:15,22-23`, `Cabinet.tsx:28`.**
XSS краде токен. Нема перевірки `exp`, нема крос-таб синхронізації (`storage` event). `Cabinet.tsx:28` читає `localStorage.getItem('token')` напряму замість `token` з модуля — роздвоєння джерела.

**F-Major-18.** `Content-Type: application/json` навіть на GET → зайвий CORS-preflight. `res.json().catch(()=>({}))` ковтає не-JSON. Читається лише `{error}`, бекенд-формат `{message,errors[]}` (ModelState) губиться. Нема `timeout/retry/AbortSignal`.

**F-Major-19.** Шляхи розкидані: обгортки `fetchServices` + сирі `api('/api/account/...')` в `Cabinet/Admin`. Дублювання префікса `/api`. Винести `API_ROUTES` + один `httpClient`.

## 2.5. UX по екранах

**Booking.tsx — головний флоу, 5 кроків:**
* Виганяє в `/login` на кроці 4 після вибору всього `if(!user) nav('/login')` — вибір втрачається. Потрібні `location.state.returnUrl` + персист `serviceId/masterId/slot` в `sessionStorage`.
* Дати крихкі: `s.start.slice(0,10)` — зламається при форматі без `T`; `new Date(day+'T12:00')` — хак проти TZ-зсуву; рендер `toLocaleTimeString` — роз'їзд з серверною TZ. Групувати по `DateOnly` з API, форматувати через `Intl.DateTimeFormat('uk-UA',{timeZone:'Europe/Kyiv'})`.
* Код без `resend/таймера 60с/maxLength=6`. Нема спінерів/skeleton — при повільному `slots` пустий екран.

**Cabinet.tsx:**
* `preferredChannel` є в типі, але нема інпута — мертве поле. `save()` без `try/catch/disabled`, лише «збережено ✓». `cancel()` без `confirm` і без rollback при помилці. Історія — гола `<table>` без пагінації/сортування.

**Admin.tsx:**
* Форма послуги не редагує `isActive` (UI нема, PUT шле старе). Форма майстра не редагує `bio/isActive`.
* `datetime-local defaultValue+onBlur` — неконтрольований, без TZ/секунд, без revert при помилці `reschedule` (а reschedule і так без валідації на беку — подвійний ризик).
* Статус — сирий enum англійською vs укр. лейбли в кабінеті. Нема пошуку/фільтра/пагінації. `confirm()` — нативний блокуючий.
* Таби `{{...}[t as 'services']}` — костиль, впаде при перейменуванні ключа.

**Login.tsx:** нема `maxLength` телефону, нема нормалізації `+380`, нема таймера повторної відправки, пароль адміна без `autocomplete="current-password"`.

## 2.6. Стилі і A11y

* Таблиці без `overflow-x` обгортки — на `<700px` ламають layout. Єдиний брейкпоінт `700px`. Нема `.btn:disabled`, `:focus-visible`, стилів `textarea`.
* A11y: `label` без `htmlFor/id`, помилки `<p class=error>` без `role=alert/aria-live`, слоти без `aria-pressed`, таби без `role=tablist/aria-selected`, нема `<form onSubmit>` (Enter не сабмітить), `nav` без `aria-label`, нема skip-link і менеджменту фокусу при зміні роута.
* Контраст: `--muted:#87888c`, `rgba(255,255,255,.6)` на склі/фото — ймовірний провал WCAG AA. `Pending #ffc94d` на світлому склі блідий.
