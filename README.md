# Gozon

Микросервисная платформа заказов и платежей на .NET с RabbitMQ, PostgreSQL, API Gateway, Swagger и WebSocket‑уведомлениями. Используются outbox/inbox‑паттерны, идемпотентная обработка сообщений и реальное время для обновления статусов заказов.

## Содержание
- [Сервисы](#сервисы)
- [Архитектура и сценарий оплаты](#архитектура-и-сценарий-оплаты)
- [Запуск проекта](#запуск-проекта)
- [Frontend](#frontend)
- [Порты и доступы](#порты-и-доступы)
- [Swagger и API](#swagger-и-api)
- [WebSocket уведомления](#websocket-уведомления)
- [Модель данных](#модель-данных)
- [Идемпотентность и гарантии](#идемпотентность-и-гарантии)
- [Масштабирование](#масштабирование)
- [Postman](#postman)
- [Структура проекта](#структура-проекта)
- [Ключевые особенности](#ключевые-особенности)

## Сервисы

| Сервис | Назначение |
| --- | --- |
| Gozon.Payments | Создание счета, пополнение, просмотр баланса |
| Gozon.Orders | Создание заказа, список заказов, статус заказа |
| Gozon.Gateway | API Gateway (YARP), маршрутизация запросов |
| frontend | Минимальный, но удобный веб-интерфейс |
| RabbitMQ | Брокер сообщений для коммуникации сервисов |
| PostgreSQL | Хранилище для Orders и Payments |


## Запуск проекта

```bash
cd gozon-online-shop
docker compose up -d --build
```


## Frontend

Минимальный интерфейс для демонстрации:
- создание счета, пополнение, баланс;
- создание заказа и список заказов;
- push-уведомления и обновление статуса в реальном времени.

Доступен на `http://localhost:8088`


## Порты и доступы

| Компонент | URL |
| --- | --- |
| Gateway | http://localhost:8080 |
| Payments API | http://localhost:8081 |
| Orders API | http://localhost:8082 |
| Frontend | http://localhost:8088 |
| RabbitMQ UI | http://localhost:15672 (guest/guest) |

## Swagger и API

Swagger включен всегда и отображает XML-комментарии контроллеров:
- Payments: `http://localhost:8081/swagger`
- Orders: `http://localhost:8082/swagger`
- Через gateway: `http://localhost:8080/payments/swagger`, `http://localhost:8080/orders/swagger`

### Примеры запросов (через Gateway)
Создать счет:
```bash
curl -X POST http://localhost:8080/payments/api/accounts -H "X-User-Id: user-1"
```

Пополнить счет:
```bash
curl -X POST http://localhost:8080/payments/api/accounts/topup \
  -H "Content-Type: application/json" -H "X-User-Id: user-1" \
  -d '{"amount": 1500}'
```

Создать заказ:
```bash
curl -X POST http://localhost:8080/orders/api/orders \
  -H "Content-Type: application/json" -H "X-User-Id: user-1" \
  -d '{"amount": 600, "description": "Подарочный набор"}'
```

Статус заказа:
```bash
curl http://localhost:8080/orders/api/orders/{orderId} -H "X-User-Id: user-1"
```

Важно: для всех запросов требуется `X-User-Id` (или `user_id` в query string).

## WebSocket уведомления

Orders Service отправляет push-уведомления о смене статуса заказа.
Подключение (через gateway):

```
ws://localhost:8080/orders/ws?userId=user-1&orderId=<orderId>
```

Во фронтенде подключение выполняется автоматически после создания заказа.

## Модель данных

Заказ:
```
Order: { id, user_id, amount, description, status, created_at, updated_at }
```

Статусы заказов:
- `NEW` — создан, ожидает оплаты;
- `FINISHED` — оплата успешна;
- `CANCELLED` — оплата не удалась (нет счета или недостаточно средств).

## Идемпотентность и гарантии

- Все события доставляются at-least-once.
- Payments использует inbox для дедупликации и идемпотентного списания.
- Orders обновляет статус идемпотентно (меняет только NEW -> FINISHED/CANCELLED).
- Списание средств выполняется в транзакции с блокировкой записи счета.

## Масштабирование

WebSocket push корректно работает при нескольких инстансах Orders:

```bash
docker compose up -d --scale orders-api=2
```

Рассылка реализована через RabbitMQ fanout exchange `orders.status`.

## Структура проекта

```
Gozon/
├─ frontend/                            # UI (веб-клиент)
├─ Gozon.Gateway/                       # единая точка входа (gateway)
│
├─ Gozon.Orders/                        # сервис заказов
│  └─ src/
│     ├─ Gozon.Orders.Api/              # транспорт: HTTP endpoints, controllers, DTO
│     ├─ Gozon.Orders.Domain/           # предметная область: сущности, правила, события
│     └─ Gozon.Orders.Infrastructure/   # БД, очереди, репозитории, внешние интеграции
│
├─ Gozon.Payments/                      # сервис платежей
│  └─ src/
│     ├─ Gozon.Payments.Api/            # транспорт: HTTP endpoints, controllers, DTO
│     ├─ Gozon.Payments.Core/           # бизнес-логика/юзкейсы (ядро)
│     └─ Gozon.Payments.Infrastructure/ # БД, очереди, репозитории, внешние интеграции
│
├─ postman/                             # коллекции Postman для тестирования API
└─ docker-compose.yml                   # запуск окружения (сервисы/брокер/БД и т.д.)

```

## Ключевые особенности
- Четкое разделение ответственности между Orders и Payments.
- RabbitMQ с семантикой at-least-once, идемпотентность в бизнес-логике.
- Transactional Outbox в Orders, Transactional Inbox + Outbox в Payments.
- Эффективно exactly-once списание для заказа (повтор не списывает средства).
- Защита баланса от гонок через транзакции и блокировки строк.
- WebSocket push для статусов заказов, работает при нескольких инстансах.
- Gateway для единой точки входа и простого проброса API.

