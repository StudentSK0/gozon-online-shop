using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Gozon.Orders.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace Gozon.Orders.Infrastructure.Persistence
{
    /// <summary>
    /// Доступ к хранилищу заказов, inbox и outbox таблицам.
    /// </summary>
    public class OrdersStore
    {
        private readonly string _connectionString;

        /// <summary>
        /// Создает хранилище на базе строки подключения PostgreSQL.
        /// </summary>
        /// <param name="connectionString">Строка подключения к БД.</param>
        public OrdersStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Создает необходимые таблицы и миграции схемы при старте.
        /// </summary>
        public async Task InitializeAsync()
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var commands = new[]
            {
                @"CREATE TABLE IF NOT EXISTS orders (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    amount BIGINT NOT NULL,
                    description TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL,
                    updated_at TIMESTAMPTZ NOT NULL
                );",
                @"ALTER TABLE orders ADD COLUMN IF NOT EXISTS description TEXT NOT NULL DEFAULT '';",
                @"CREATE TABLE IF NOT EXISTS inbox_messages (
                    message_id TEXT PRIMARY KEY,
                    type TEXT NOT NULL,
                    payload JSONB NOT NULL,
                    received_at TIMESTAMPTZ NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS outbox_messages (
                    message_id TEXT PRIMARY KEY,
                    type TEXT NOT NULL,
                    payload JSONB NOT NULL,
                    occurred_at TIMESTAMPTZ NOT NULL,
                    processed_at TIMESTAMPTZ NULL
                );"
            };

            foreach (var commandText in commands)
            {
                await using var command = new NpgsqlCommand(commandText, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Создает заказ и записывает outbox-запрос на оплату в одной транзакции.
        /// </summary>
        /// <param name="userId">Пользователь, создающий заказ.</param>
        /// <param name="amount">Сумма заказа в целых единицах валюты.</param>
        /// <param name="description">Описание заказа.</param>
        /// <returns>Созданный заказ.</returns>
        public async Task<Order> CreateOrderAsync(string userId, long amount, string description)
        {
            var order = new Order
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Amount = amount,
                Description = description,
                Status = OrderStatus.NEW,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var paymentRequest = new PaymentRequestMessage
            {
                MessageId = order.Id,
                OrderId = order.Id,
                UserId = order.UserId,
                Amount = order.Amount
            };

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var insertOrder = new NpgsqlCommand(
                @"INSERT INTO orders (id, user_id, amount, description, status, created_at, updated_at)
                  VALUES (@id, @user_id, @amount, @description, @status, @created_at, @updated_at);",
                connection,
                transaction))
            {
                insertOrder.Parameters.AddWithValue("id", order.Id);
                insertOrder.Parameters.AddWithValue("user_id", order.UserId);
                insertOrder.Parameters.AddWithValue("amount", order.Amount);
                insertOrder.Parameters.AddWithValue("description", order.Description);
                insertOrder.Parameters.AddWithValue("status", order.Status.ToString());
                insertOrder.Parameters.AddWithValue("created_at", order.CreatedAt);
                insertOrder.Parameters.AddWithValue("updated_at", order.UpdatedAt);
                await insertOrder.ExecuteNonQueryAsync();
            }

            await using (var insertOutbox = new NpgsqlCommand(
                @"INSERT INTO outbox_messages (message_id, type, payload, occurred_at)
                  VALUES (@message_id, @type, @payload, @occurred_at);",
                connection,
                transaction))
            {
                insertOutbox.Parameters.AddWithValue("message_id", paymentRequest.MessageId);
                insertOutbox.Parameters.AddWithValue("type", "PaymentRequested");
                insertOutbox.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(paymentRequest);
                insertOutbox.Parameters.AddWithValue("occurred_at", DateTimeOffset.UtcNow);
                await insertOutbox.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return order;
        }

        /// <summary>
        /// Возвращает заказ по идентификатору.
        /// </summary>
        /// <param name="orderId">Идентификатор заказа.</param>
        /// <returns>Заказ или null, если не найден.</returns>
        public async Task<Order> GetOrderAsync(string orderId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                @"SELECT id, user_id, amount, description, status, created_at, updated_at
                  FROM orders WHERE id = @id;",
                connection);
            command.Parameters.AddWithValue("id", orderId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new Order
            {
                Id = reader.GetString(0),
                UserId = reader.GetString(1),
                Amount = reader.GetInt64(2),
                Description = reader.GetString(3),
                Status = ParseStatus(reader.GetString(4)),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
            };
        }

        /// <summary>
        /// Возвращает все заказы пользователя, отсортированные по дате создания.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя.</param>
        /// <returns>Список заказов.</returns>
        public async Task<IReadOnlyList<Order>> GetOrdersByUserAsync(string userId)
        {
            var results = new List<Order>();

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                @"SELECT id, user_id, amount, description, status, created_at, updated_at
                  FROM orders WHERE user_id = @user_id ORDER BY created_at DESC;",
                connection);
            command.Parameters.AddWithValue("user_id", userId);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new Order
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Amount = reader.GetInt64(2),
                    Description = reader.GetString(3),
                    Status = ParseStatus(reader.GetString(4)),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                    UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
                });
            }

            return results;
        }

        /// <summary>
        /// Читает партию неотправленных outbox сообщений.
        /// </summary>
        /// <param name="limit">Максимальное число сообщений.</param>
        /// <returns>Ожидающие сообщения outbox.</returns>
        public async Task<IReadOnlyList<OutboxMessage>> GetPendingOutboxAsync(int limit)
        {
            var results = new List<OutboxMessage>();

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                @"SELECT message_id, type, payload
                  FROM outbox_messages
                  WHERE processed_at IS NULL
                  ORDER BY occurred_at
                  LIMIT @limit;",
                connection);
            command.Parameters.AddWithValue("limit", limit);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new OutboxMessage
                {
                    MessageId = reader.GetString(0),
                    Type = reader.GetString(1),
                    Payload = reader.GetString(2)
                });
            }

            return results;
        }

        /// <summary>
        /// Помечает outbox сообщение как обработанное.
        /// </summary>
        /// <param name="messageId">Идентификатор сообщения.</param>
        public async Task MarkOutboxProcessedAsync(string messageId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                "UPDATE outbox_messages SET processed_at = @processed_at WHERE message_id = @message_id;",
                connection);
            command.Parameters.AddWithValue("processed_at", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("message_id", messageId);

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Применяет результат оплаты идемпотентно и возвращает обновленный заказ.
        /// </summary>
        /// <param name="result">Сообщение о результате оплаты.</param>
        /// <returns>Обновленный заказ или null, если сообщение уже обработано.</returns>
        public async Task<Order> ApplyPaymentResultAsync(PaymentResultMessage result)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var inboxInserted = await TryInsertInboxAsync(connection, transaction, result);
            if (!inboxInserted)
            {
                await transaction.CommitAsync();
                return null;
            }

            var newStatus = string.Equals(result.Status, "Paid", StringComparison.OrdinalIgnoreCase)
                ? OrderStatus.FINISHED
                : OrderStatus.CANCELLED;

            await using var update = new NpgsqlCommand(
                @"UPDATE orders
                  SET status = @status, updated_at = @updated_at
                  WHERE id = @id AND status = @current_status
                  RETURNING id, user_id, amount, description, status, created_at, updated_at;",
                connection,
                transaction);
            update.Parameters.AddWithValue("status", newStatus.ToString());
            update.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);
            update.Parameters.AddWithValue("id", result.OrderId);
            update.Parameters.AddWithValue("current_status", OrderStatus.NEW.ToString());

            Order updated = null;
            await using (var reader = await update.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    updated = new Order
                    {
                        Id = reader.GetString(0),
                        UserId = reader.GetString(1),
                        Amount = reader.GetInt64(2),
                        Description = reader.GetString(3),
                        Status = ParseStatus(reader.GetString(4)),
                        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
                    };
                }
            }

            await transaction.CommitAsync();
            return updated;
        }

        private static async Task<bool> TryInsertInboxAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PaymentResultMessage result)
        {
            await using var command = new NpgsqlCommand(
                @"INSERT INTO inbox_messages (message_id, type, payload, received_at)
                  VALUES (@message_id, @type, @payload, @received_at)
                  ON CONFLICT (message_id) DO NOTHING;",
                connection,
                transaction);
            command.Parameters.AddWithValue("message_id", result.MessageId);
            command.Parameters.AddWithValue("type", "PaymentResult");
            command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(result);
            command.Parameters.AddWithValue("received_at", DateTimeOffset.UtcNow);

            var rows = await command.ExecuteNonQueryAsync();
            return rows == 1;
        }

        private static OrderStatus ParseStatus(string value)
        {
            if (Enum.TryParse<OrderStatus>(value, true, out var status))
            {
                return status;
            }

            return OrderStatus.NEW;
        }
    }

    /// <summary>
    /// DTO строки outbox, ожидающей публикации.
    /// </summary>
    public class OutboxMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }
}
