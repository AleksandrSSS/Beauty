# Блок B — фото: зберігання (Фази 2, 3)

Джерело: `masters-feature-plan.md` §8, §9. Залежність: блок A (потрібна `Migrate()`).

## Мета

Колонка `PhotoUrl` + безпечний upload зі статикою. Після блока адмін може завантажити фото, публіка бачить шлях у API.

## Кроки

### B1. Поле `PhotoUrl` (§8)

1. `server/Models/Entities.cs`, клас `Master` після `IsActive`:
   `public string? PhotoUrl { get; set; }` + XML-коментар «відносний шлях, null → ініціали».
2. `dotnet ef migrations add AddMasterPhotoUrl --project server`, `dotnet ef database update --project server`.
3. Контроль: `\d "Masters"` → `PhotoUrl text NULL`.
4. `CatalogController.Masters()` + `AdminController.Masters()` — додати `m.PhotoUrl` у видачу.
5. `AdminController.MasterDto` — новий параметр `string? PhotoUrl` **останнім** (позиційний record). В `AddMaster`/`UpdateMaster`: `if (dto.PhotoUrl != null) m.PhotoUrl = dto.PhotoUrl;` — обнулення тільки через DELETE-ендпоінт, інакше стара форма без поля зітре фото.
6. Перевірка: обидва GET повертають `"photoUrl": null`; PUT старим body не падає і фото не чіпає.

### B2. Конфіг і статика (§9.1, §9.2)

1. `server/appsettings.json` після `"Salon"`: блок `Uploads { MastersPath: wwwroot/uploads/masters, MaxFileSizeBytes: 3145728 }`.
2. `server/Program.cs` після `UseCors()` до `MapControllers()`: `app.UseStaticFiles();`.
3. `server/wwwroot/uploads/masters/.gitkeep`.
4. Кореневий `.gitignore` доповнити: `server/wwwroot/uploads/masters/*`, `!.gitkeep`, `db-backup-*.sql`.

### B3. Ендпоінти (§9.3–9.5)

1. `AdminController`: інжектувати `IWebHostEnvironment env`, `IConfiguration cfg` у primary constructor (клас уже `[Authorize(Roles="Admin")]`).
2. `POST /api/admin/masters/{id}/photo` (multipart, поле `file`):
   `[RequestSizeLimit(3*1024*1024)]` + перевірка `file.Length` у коді; whitelist `.jpg/.jpeg/.png/.webp` по `Path.GetExtension().ToLowerInvariant()`; `ContentType` від клієнта ігнорувати; magic bytes перших 12 байт (JPEG `FF D8 FF`, PNG `89 50 4E 47...`, WebP `RIFF....WEBP`); ім'я генерує сервер `{id}-{Guid:N}{ext}`, `file.FileName` не використовувати взагалі; `Directory.CreateDirectory`; старий файл видаляти після успішного запису в `try/catch` без провалу запиту; `m.PhotoUrl = "/uploads/masters/{name}"`.
3. `DELETE /api/admin/masters/{id}/photo` → файл видалити якщо є, `PhotoUrl = null`, ідемпотентний 200.
4. `DeleteMaster`: hard-гілка видаляє файл, soft-гілка лишає.
5. Коди: 400 з українськими текстами з таблиці §9.3, 404 майстра, 401/403.

### B4. Клієнтський доступ (§9.6)

1. `client/vite.config.ts`: додати проксі `'/uploads': 'http://localhost:5000'` як страховку (основний шлях — склейка з `VITE_API_URL` у блоці D).
2. `README.md`: розділ «Бекап» — дві частини разом (`pg_dump` + архів `server/wwwroot/uploads`), попередження що в БД лише шляхи.

## Перевірка блока

1. Upload jpg/png/webp до 3 МБ → 200, файл у папці, `PhotoUrl` у БД, відкривається `http://localhost:5000/uploads/masters/<name>`.
2. 5 МБ → 400 про розмір. `.txt` як `.jpg` → 400 про вміст. `.exe` → 400 про розширення.
3. Повторний upload — старий файл зник.
4. DELETE — файл зник, `PhotoUrl = null`.
5. Без токена адміна → 401/403.
6. `filename="../../../appsettings.json"` через curl — збережено під згенерованим ім'ям, поза папкою нічого нема.
7. `dotnet build server`, `dotnet ef migrations list --project server`.

## Відкат

`git revert` + `UPDATE "Masters" SET "PhotoUrl" = NULL` + чистка папки. Колонку можна лишити (nullable).

Оцінка: 3,5–6 год.
