# Slot Math Lab

Конструктор математических моделей слот-игр на .NET 10 и React 19: приватные
проекты, редактируемые графы, Reel/Paytable Studio, State Machine Designer,
точные расчёты, Монте-Карло, измерения, воспроизведение и архив результатов.

Подробное описание реализованной системы, её ограничений, запуска и проверок:
**[SYSTEM.ru.md](docs/SYSTEM.ru.md)**.

## Быстрый запуск

Нужны .NET 10 SDK, Node.js 22 и Docker Compose. Из корня репозитория:

```sh
docker compose up -d postgres redis
dotnet build backend/SlotMathLab.slnx
npm --prefix frontend ci --legacy-peer-deps
```

Первый терминал:

```sh
ConnectionStrings__DefaultConnection='Host=localhost;Port=5433;Database=slotmath;Username=slotmath;Password=slotmath' \
  dotnet run --project backend/SlotMath.Api --launch-profile http
```

Второй терминал:

```sh
npm --prefix frontend run dev
```

Откройте адрес, который вывел Vite, и начните с **My projects**.
API работает на `http://localhost:5152`. Порты локального Docker:
PostgreSQL — 5433, Redis — 6380. Полный локальный стек:
`docker compose up -d --build`, интерфейс на `http://localhost:5199`.

## Состав

- `backend/SlotMath.Core` — числа, выражения, графы, компилятор и решатели.
- `backend/SlotMath.Api` — API, исполнение, приватные версии и результаты.
- `frontend/src` — рабочее пространство, редакторы, анализ и игровые представления.
- `backend/*.Tests`, `frontend/unit`, `frontend/e2e` — проверки.
- `scripts/verification` — независимые references, генераторы и deployment checks.

Исследовательский проект без заявленной игровой сертификации.
Происхождение и права сторонних ассетов проверяются по данным соответствующей игры.
