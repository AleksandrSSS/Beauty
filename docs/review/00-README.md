# Beauty Salon — розгорнутий аудит проекту

Дата: 2026-09-03
Обсяг: `server/` (ASP.NET Core 9 + EF Core + PostgreSQL), `client/` (React 18 + Vite + TS), `docker-compose.yml`, `test-e2e.ps1`, `ui-tests/`, `design/`, логи.

## Як читати цю папку

* `01-backend.md` — безпека, БД, бронювання, час, нотифікації. 7 Critical + 12 Major з `файл:рядок`, сценаріями атаки і прикладами фіксів.
* `02-frontend.md` — React/TS/API-клієнт/UX/A11y. 5 Critical + розбір кожного екрану.
* `03-infra-qa.md` — Docker, CI, тести, логи, кодування, Figma/design.
* `04-roadmap.md` — план P0/P1/P2 з оцінкою трудомісткості і конкретними патчами.

## Короткий вердикт

Проект — сильний MVP: правильний стек, транзакція проти double-booking, салонна timezone `Europe/Kyiv`, м'яке видалення, BCrypt.

Але до продакшену не готовий:

1. OTP `4 символи з "123456789"` = 6561 комбінацій без rate-limit — брутфорс за хвилини.
2. `devCode` повертається в API в MOCK-режимі (дефолт) — обхід підтвердження.
3. `Admin reschedule/set-status` без валідації — можна покласти записи один на один.
4. `EnsureCreated()` замість міграцій — схема не еволюціонує.
5. Фронт: нереактивний `export let user`, нема guards, баг `+''===0`, нема обробки 401.
6. Тестів 0, CI нема, Docker тільки під Postgres, секрети в проєкті.

Деталі і як лагодити — далі по файлах.

## Методологія

* Ручний перегляд всіх `.cs` в `Controllers/Data/Services/Models` + `Program.cs` + `appsettings.json`.
* Ручний перегляд всіх `.tsx/.ts` в `client/src` + `package.json` + `vite.config.ts` + `tsconfig`.
* Перевірка `docker-compose.yml`, `test-e2e.ps1`, `ui-tests/*.py`, хвостів логів.
* Severity: Critical — експлуатується / втрата даних / обхід auth; Major — баги логіки / DoS / поганий UX; Minor — якість/стиль.
