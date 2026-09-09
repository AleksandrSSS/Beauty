# 03 — Інфра, тести, логи, дизайн

## 3.1. Docker — не prod-ready

`docker-compose.yml:1-20` — тільки `postgres:16`.

Що добре: `volumes: beauty_pgdata`, `healthcheck: pg_isready -U beauty interval 5s retries 10`.

Що погано:
* Нема сервісів `server/client`, нема `Dockerfile` ніде (`Get-ChildItem Dockerfile*` — порожньо). Зібрати прод-образ неможливо, запуск тільки `dotnet run + npm run dev`.
* Хардкод `POSTGRES_PASSWORD: beauty` + `appsettings.json:3` `Password=beauty` в проєкті.
* `Jwt:Key / Admin:Password` тільки в `user-secrets` (`Program.cs:12-16` кидає `InvalidOperationException` без них) — в Docker це впаде, бо `env Jwt__Key` не прокинуто.
* `ports: 5432:5432` на хост — ок для dev, погано для prod.
* Нема `restart: unless-stopped`, `networks:`, `env_file/.env`, `depends_on: service_healthy`, лімітів ресурсів, бекапу volume.
* `ConnectionStrings:Default=Host=localhost` не працює зсередини контейнера — треба `Host=postgres`.

Мінімальний prod-compose:
```yaml
services:
  postgres: {..., restart: unless-stopped, env_file: [.env]}
  server:
    build: ./server
    environment:
      ConnectionStrings__Default: Host=postgres;Port=5432;Database=beauty;Username=beauty;Password=${PG_PASSWORD}
      Jwt__Key: ${JWT_KEY}
      Admin__Password: ${ADMIN_PASSWORD}
      Cors__AllowedOrigins__0: https://beauty.example.com
    depends_on: { postgres: { condition: service_healthy } }
  client:
    build: ./client
    depends_on: [server]
```

## 3.2. CI — відсутній

CI-конфіг відсутній. `client/package.json` scripts: тільки `dev/build/preview`, нема `test/lint`. В `server/*.csproj` нема тестового проекту, `**/*test*` знаходить тільки `node_modules`.

Наслідок: `ui-tests/run.log:3` червоний, ніхто не помітив.

Мінімум CI-процес:
`dotnet build + dotnet test` (після додавання xUnit) + `npm run build` + `playwright smoke` з Postgres-сервісом.

## 3.3. Тести — покриття ≈ 0

* Unit/integration: 0. `Controllers/Services/Data` без тестів. `src/pages/*.tsx` без `*.test.*`.
* `test-e2e.ps1:1-32` — happy-path без asserts:
  - hardcoded `masterId:1/serviceId:1`, телефон `+380991112233`, адмін `+380000000000/admin123`.
  - впаде якщо `$slots` порожній (`$slots[0]` → null).
  - залежить від `devCode` MOCK (`README:54-55`) — в проді з реальним SMS не працює.
  - нема негативних кейсів (double-book, expired, 401, reschedule-конфлікт), нема cleanup, `baseUrl` зашитий `:5000`.
* `ui-tests/ui_smoke.py:1-81` — Playwright smoke (home→login→booking→admin), але крихкий: `select >> nth=0/1, index=1`, `wait_for_timeout(500)`, укр. текстові селектори, абсолютний `shots = C:\...`, тільки `headless=True`. `recon.py` — дебажний залишок.
* `ui-tests/run.log:3`: `AssertionError: services not rendered`, селектор `text=????` — mojibake (запуск під cp1251 замість UTF-8).
* Нема `requirements.txt` для `playwright==1.62.0` (`pip.log` — тільки ручний `pip install`).

Що додати: xUnit (double-book паралельно, verify expiry, reschedule-конфлікт), vitest (групування слотів, `+''` баг), `test-e2e.ps1` з `-BaseUrl -Phone`, перевіркою `$slots.Count -gt 0`, `exit 1` на fail; `ui_smoke` на `data-testid + expect().to_be_visible()`.

## 3.4. Логи і env-дрейф

* `client_run.log`: `[vite] EBUSY: rename deps_temp → deps` (Windows file-lock) + `^C`. Лікується виключенням `node_modules/.vite` з антивіруса. `.env VITE_API_URL=` порожній, а example має `5000` + зайвий `VITE_AUTH_URL=http://localhost:3001` (сервісу нема).
* `srv_out.log`: `Now listening on http://localhost:5010`, а `launchSettings.json + vite.config.ts:6 proxy /api → :5000 + test-e2e.ps1:2` чекають `:5000`. Порт-дрейф → проксі/e2e падають. Уніфікувати `5000`, прибрати дрейф портів.
* `server_run.log`: EF `Executed DbCommand` info-шум + `ReminderWorker` кожні 5 хв. Критичне: `[Telegram MOCK] -> ... �-������?` — кирилиця бита (потрібен `chcp 65001` / UTF-8). `Logging: Default Information` занадто verbose для prod → `Warning`, EF `Information` тільки в dev.
* `pip.log`: `pip 26.1.2 → 26.2.1` оновити, залежності не зафіксовані. `pw.log`: тільки скачування Chromium 151МБ. `srv_err.log` порожній — добре.
* Системне: `design/tokens.css:1-10` коментарі биті, `PLAN.md:50-51` фіксує «Admin.tsx має зламане кодування». Всі `*.py/*.css/*.tsx` зберегти в UTF-8, запускати `chcp 65001; $OutputEncoding=[utf8]`.

## 3.5. Design / Figma

* `design/screens/` — 11 PNG по ~2МБ (~25МБ сумарно). Тримати окремо або лишити тільки лінк.
* `design/PLAN.md` — хороший мапінг макет→сторінки, але `Крок 6 — за окремою командою`, `tokens.css` не підключений до `client/src/styles.css` — дрейф дизайн/код.
* `figma_file.json` (20627 байт): всі `FRAME ... "children":[]` — геометрія без контенту. Корисне тільки: `BEAUTY SALON SYSTEM, accent FB646B, Inter-Bold, 12 фреймів (LOGIN/DASH/SETTINGS...)`. Не джерело істини, `thumbnailUrl` підписаний до `20260830` — протухне.
* `figma_1_2.png`, `figma_preview/` — дублюють `design/screens`, визначити одне місце.
