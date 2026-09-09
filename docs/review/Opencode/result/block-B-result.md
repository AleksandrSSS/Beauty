# Результат блока B — фото: зберігання (Фази 2+3). Статус: ✅ ВИКОНАНО

Дата: 2026-09-04. План: `docs/review/Kiro/masters-feature-plan.md` §8, §9.

## B1. Поле PhotoUrl (§8)

- `Models/Entities.cs`: `Master.PhotoUrl string?` + XML-коментар.
- `dotnet ef migrations add AddMasterPhotoUrl` + `database update` → `ALTER TABLE "Masters" ADD "PhotoUrl" text` ✅ (DDL як очікувалось).
- `migrations list`: `InitialCreate`, `AddMasterPhotoUrl` — обидві застосовані.
- `CatalogController.Masters()` і `AdminController.Masters()` повертають `photoUrl` (до завантажень — `null`).
- `MasterDto.PhotoUrl` — останнім параметром; `AddMaster` пише напряму; `UpdateMaster` — тільки `if (dto.PhotoUrl != null)` (обнулення лише через DELETE).
- Перевірка: PUT старим body без `photoUrl` → 200, фото майстра №2 не затерлось (перевірено наживо).

## B2. Конфіг і статика (§9.1, §9.2)

- `appsettings.json`: блок `Uploads { MastersPath, MaxFileSizeBytes: 3145728 }`.
- `Program.cs`: `app.UseStaticFiles()` між `UseCors()` і `MapControllers()`.
- `server/wwwroot/uploads/masters/.gitkeep`; `.gitignore` += `uploads/masters/*`, `!.gitkeep`, `db-backup-*.sql`.

## B3. Ендпоінти (§9.3–9.5)

- `AdminController`: інжект `IWebHostEnvironment` + `IConfiguration`.
- `POST masters/{id}/photo`: `[RequestSizeLimit(3МБ)]` + перевірка розміру в коді; whitelist `.jpg/.jpeg/.png/.webp`; `ContentType` ігнорується; magic bytes JPG/PNG/WebP (RIFF....WEBP); ім'я `{id}-{Guid:N}{ext}`, `file.FileName` не використовується; старий файл чиститься після запису в try/catch; коди за таблицею §9.3.
- `DELETE masters/{id}/photo`: ідемпотентний 200, `PhotoUrl = null`.
- `DeleteMaster` hard-гілка: видаляє файл фото; soft-гілка лишає.
- `TryDeletePhotoFile`: тільки всередині папки uploads (`GetFullPath` + `StartsWith`), мовчить про помилки.
- Білд: **0 Warning, 0 Error**.

## B4. Клієнтський доступ + доки (§9.6, §9.7)

- `vite.config.ts`: проксі `'/uploads' → :5000` (страховка; основний шлях — `VITE_API_URL` у блоці D).
- `README.md`: розділ «Бекап» (двокроковий).
- `npm run build` — OK.

## Приймання §9.8 — 8/8 ✅

1. Upload PNG 67 байт → 200 `{"photoUrl":"/uploads/masters/1-....png"}`, файл у папці, `PhotoUrl` у БД, `GET /uploads/...` → 200 (67 байт).
2. Файл 5 МБ → запит відхилено ще на рівні фреймворку (`RequestSizeLimit` → **413 Payload Too Large**, тіло «Request body too large... 3145728 bytes»); гілка коду `file.Length > maxBytes → 400` при рівному конфігу й атрибуту фактично недосяжна (спрацює лише якщо ліміт у конфігу знизити нижче атрибута). Файл не зберігається в обох випадках.
3. `.txt` як `.jpg` → 400 `«Вміст файлу не є зображенням...»`.
4. `.exe` → 400 `«Дозволені лише файли JPG, PNG або WebP»`.
5. Повторний upload → старий файл зник з папки, новий на місці.
6. DELETE → файл зник, `photoUrl: null`.
7. Без токена → **401**.
8. `filename="../../../evil.png"` через curl → збережено під згенерованим ім'ям, поза папкою нічого нема.

## Інцидент під час перевірки (виправлено)

PUT-тест старим body тимчасово змінив майстра №2 (якір + години). Відновив з ранкового бекапа: `rotationAnchor = 2026-09-02`, 7 днів 09:00–19:00, послуги 1,2,3, `photoUrl = null`. Папка uploads чиста (тільки `.gitkeep`). БД даних клієнтів не торкався.

## Середовищні нотатки

- `Invoke-RestMethod -Form` нема в PS 5.1 — multipart тести ганяв через `curl.exe`.
- `curl.exe` за дефолтом йде через корпоративний проксі → для localhost обов'язково `--noproxy localhost`.
- Кавички в inline-psql через цей шелл губляться — SQL тільки файлами через pipe (`counts.sql`, `cleanup-verify.sql` — у тимчасовій теці).

## Файли (змінено/створено)

Змінено: `Models/Entities.cs`, `Controllers/CatalogController.cs`, `Controllers/AdminController.cs`, `Program.cs`, `appsettings.json`, `client/vite.config.ts`, `README.md`, `.gitignore`.
Створено: `server/Migrations/*AddMasterPhotoUrl*`, `server/wwwroot/uploads/masters/.gitkeep`.

## Наступний крок

Блок C (Фаза 4): `ScheduleService` + D1–D4 + регресія слотів.
