# WarehouseApi + PostgreSQL — учёт складских товаров

Контрольная точка: API + PostgreSQL в Docker Compose.
Проектная тема — **учёт складских товаров**. Сущность CRUD — **товар (Product)**:
артикул, название, остаток, минимальный остаток, цена. Это основная запись складского учёта —
по ней ведутся остатки, приход и расход.

Стек: .NET 10, ASP.NET Core Minimal API, EF Core + Npgsql, PostgreSQL 16, Docker Compose.

```
API (warehouse-api, :8080)  ──Host=db:5432──▶  PostgreSQL (warehouse-db)
                                                   │
                                            volume db_data
                                     (/var/lib/postgresql/data)
```

## Структура

```
WarehouseApiPostgres/
├── Program.cs              — endpoint'ы (CRUD + приход/расход)
├── Models/Product.cs       — сущность и тела запросов
├── Data/WarehouseDb.cs     — DbContext EF Core, создание таблицы, стартовые данные
├── appsettings.json        — строка подключения для локального запуска, уровни логов
├── WarehouseApi.csproj
├── Dockerfile              — сборка образа API (multi-stage)
├── docker-compose.yml      — сервисы api и db, сеть, volume, переменные окружения
├── check.sh                — полный прогон с сохранением выводов в docs/
└── docs/                   — подтверждения: compose ps, CRUD, перезапуск, logs
```

## Endpoint'ы

| Метод | Путь | Операция |
|---|---|---|
| GET | `/products` | **Read** — все товары |
| GET | `/products/{id}` | **Read** — один товар |
| POST | `/products` | **Create** — новый товар |
| PUT | `/products/{id}` | **Update** — полное обновление товара |
| DELETE | `/products/{id}` | **Delete** — удаление товара |
| GET | `/products/low-stock` | товары с остатком ниже минимального |
| POST | `/products/{id}/receive` | приход `{"amount": N}` |
| POST | `/products/{id}/issue` | расход `{"amount": N}`, в минус нельзя |
| GET | `/health` | проверка API и подключения к БД |

Тело для POST/PUT:

```json
{ "sku": "GLUE-1", "name": "Клей ПВА 1 кг", "quantity": 40, "minQuantity": 10, "price": 210 }
```

## docker-compose.yml

| | |
|---|---|
| Сервисы | `db` — `postgres:16`; `api` — собирается из `Dockerfile` |
| База | `app`, пользователь `postgres`, пароль `1234` |
| Переменные окружения | у `db`: `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`; у `api`: `ConnectionStrings__Default`, `ASPNETCORE_HTTP_PORTS` |
| Подключение API к БД | `Host=db;Port=5432;Database=app;Username=postgres;Password=1234;Gss Encryption Mode=Disable` — `db` это имя сервиса, Docker DNS резолвит его внутри сети |
| Сеть | `warehouse_net` (bridge), в ней оба сервиса |
| depends_on | `api` ждёт `db` с `condition: service_healthy`; healthcheck у `db` — `pg_isready` |
| Volume | `db_data` → `/var/lib/postgresql/data`, данные переживают `docker compose down` |
| Порты | наружу только API: `8080:8080`; PostgreSQL доступна только внутри сети |

Почему PostgreSQL, а не SQLite: база работает отдельным сервисом со своим контейнером и
своим хранилищем, API подключается к ней по сети — так устроены реальные системы, и
так требует задание. ASP.NET читает `ConnectionStrings__Default` из окружения
(двойное подчёркивание = вложенность `ConnectionStrings:Default`), поэтому строка в
`appsettings.json` нужна только для локального запуска без Docker.

`Gss Encryption Mode=Disable` — отключает попытку Kerberos-шифрования: в образе `aspnet` нет
библиотеки `libgssapi_krb5`, и без этого флага Npgsql пишет в лог ошибку о ней при каждом подключении.

При старте API создаёт таблицу `products`, если её нет (`EnsureCreated`), и заливает
4 стартовых товара **только если таблица пустая**. После перезапуска в логах видно
`Database already has N products, seed skipped` — данные взяты из volume.

## Запуск

```bash
docker compose up -d --build
docker compose ps
```

В `docker compose ps` должны быть `warehouse-api` (Up) и `warehouse-db` (Up, healthy).

## CRUD-проверка (curl)

```bash
# Read
curl http://localhost:8080/products
curl http://localhost:8080/products/1

# Create
curl -X POST http://localhost:8080/products \
  -H 'Content-Type: application/json' \
  -d '{"sku":"GLUE-1","name":"Клей ПВА 1 кг","quantity":40,"minQuantity":10,"price":210}'

# Update
curl -X PUT http://localhost:8080/products/5 \
  -H 'Content-Type: application/json' \
  -d '{"sku":"GLUE-1","name":"Клей ПВА 1 кг","quantity":55,"minQuantity":10,"price":199.90}'

# Delete
curl -i -X DELETE http://localhost:8080/products/4
```

## Проверка сохранения данных после перезапуска

```bash
curl http://localhost:8080/products        # запомнить, что в базе
docker compose down                        # контейнеры удалены, volume db_data остаётся
docker compose up -d
curl http://localhost:8080/products        # те же данные

# напрямую из PostgreSQL
docker compose exec db psql -U postgres -d app -c "SELECT * FROM products ORDER BY id;"
```

`docker compose down -v` удалит и volume — тогда база начнётся с нуля.

## Логи

```bash
docker compose logs
docker compose logs api
docker compose logs db
```

В логах API видны SQL-команды к PostgreSQL (`INSERT`, `UPDATE`, `DELETE`) — подтверждение,
что данные пишутся в БД.

## Всё одной командой

```bash
chmod +x check.sh
./check.sh
```

Скрипт поднимает систему с чистой базы, делает CRUD, перезапускает через down/up,
проверяет данные и сохраняет выводы в `docs/`:

| Файл | Что внутри |
|---|---|
| `compose_up.txt` | вывод `docker compose up --build` |
| `compose_ps.txt` | `docker compose ps` до и после перезапуска |
| `crud.txt` | CRUD-запросы и ответы |
| `persistence.txt` | данные до/после `down`/`up` + `SELECT` из PostgreSQL |
| `compose_logs_run1.txt` | логи первого запуска (CRUD) |
| `compose_logs_run2.txt` | логи после перезапуска (seed skipped) |

## Остановка

```bash
docker compose down        # данные сохраняются
docker compose down -v     # вместе с volume db_data
```
