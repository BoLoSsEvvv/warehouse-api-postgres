#!/usr/bin/env bash
# Полный прогон КТ: compose up -> ps -> CRUD -> down/up -> проверка сохранения -> logs.
# Выводы складываются в docs/*.txt — это подтверждения для сдачи.
set -uo pipefail
cd "$(dirname "$0")"
mkdir -p docs

URL=http://localhost:8080
JSON='Content-Type: application/json'

# Контейнер с прошлой КТ занимает порт 8080 — убираем его, если запущен.
docker rm -f warehouse >/dev/null 2>&1 || true

# Чистый старт: сносим и контейнеры, и volume, чтобы прогон был воспроизводимым.
docker compose down -v >/dev/null 2>&1 || true

wait_api() {
  for _ in $(seq 1 60); do
    curl -sf "$URL/health" >/dev/null && return 0
    sleep 1
  done
  echo "API не поднялось за 60 секунд"; docker compose logs api; exit 1
}

run() { echo "\$ $*"; eval "$@"; echo; echo; }

echo "===== docker compose up ====="
docker compose up -d --build 2>&1 | tee docs/compose_up.txt
wait_api

echo
echo "===== docker compose ps ====="
docker compose ps | tee docs/compose_ps.txt

echo
echo "===== CRUD ====="
{
  echo "### READ: все товары"
  run "curl -s $URL/products"

  echo "### CREATE: новый товар"
  run "curl -s -X POST $URL/products -H '$JSON' -d '{\"sku\":\"GLUE-1\",\"name\":\"Клей ПВА 1 кг\",\"quantity\":40,\"minQuantity\":10,\"price\":210}'"

  echo "### READ: созданный товар"
  run "curl -s $URL/products/5"

  echo "### UPDATE: меняем остаток и цену"
  run "curl -s -X PUT $URL/products/5 -H '$JSON' -d '{\"sku\":\"GLUE-1\",\"name\":\"Клей ПВА 1 кг\",\"quantity\":55,\"minQuantity\":10,\"price\":199.90}'"

  echo "### DELETE: удаляем скотч (id=4)"
  run "curl -s -o /dev/null -w 'HTTP %{http_code}' -X DELETE $URL/products/4"

  echo "### READ: удалённый товар -> 404"
  run "curl -s $URL/products/4"

  echo "### Приход гаек на склад"
  run "curl -s -X POST $URL/products/2/receive -H '$JSON' -d '{\"amount\":400}'"

  echo "### READ: итоговый список"
  run "curl -s $URL/products"
} | tee docs/crud.txt

echo
echo "===== Перезапуск и проверка сохранения ====="
{
  echo "### Данные ДО перезапуска"
  run "curl -s $URL/products"

  # down удалит контейнеры вместе с их логами — сохраняем логи первого запуска (там CRUD)
  docker compose logs --no-color > docs/compose_logs_run1.txt 2>&1

  run "docker compose down"
  echo "(контейнеры удалены, volume db_data остался)"
  run "docker volume ls --filter name=db_data"

  run "docker compose up -d"
  wait_api

  echo "### Данные ПОСЛЕ перезапуска — GLUE-1 на месте, TAPE-50 удалён, гаек 700"
  run "curl -s $URL/products"

  echo "### То же самое прямо из PostgreSQL"
  run "docker compose exec -T db psql -U postgres -d app -c 'SELECT id, sku, name, quantity, min_quantity, price FROM products ORDER BY id;'"
} | tee docs/persistence.txt

echo
echo "===== docker compose ps (после перезапуска) ====="
docker compose ps | tee -a docs/compose_ps.txt

echo
echo "===== docker compose logs (после перезапуска) ====="
docker compose logs --no-color 2>&1 | tee docs/compose_logs_run2.txt

echo
echo "Готово. Выводы в docs/. Система оставлена запущенной (docker compose down — остановить)."
