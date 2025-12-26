using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Gozon.Payments.Core.Models;
using Npgsql;
using NpgsqlTypes;

namespace Gozon.Payments.Infrastructure.Persistence
{
    /// <summary>
    /// Доступ к данным платежей, счетов и outbox/inbox таблицам.
    /// </summary>
    public class PaymentsStore
    {
        private readonly string _connectionString;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Создает хранилище на базе строки подключения PostgreSQL.
        /// </summary>
        /// <param name="connectionString">Строка подключения к БД.</param>
        public PaymentsStore(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Создает необходимые таблицы при старте сервиса.
        /// </summary>
        public async Task InitializeAsync()
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var commands = new[]
            {
                @"CREATE TABLE IF NOT EXISTS accounts (
                    user_id TEXT PRIMARY KEY,
                    balance BIGINT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS account_transactions (
                    id UUID PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    amount BIGINT NOT NULL,
                    reference TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS payment_operations (
                    order_id TEXT PRIMARY KEY,
                    message_id TEXT NOT NULL UNIQUE,
                    user_id TEXT NOT NULL,
                    amount BIGINT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL
                );",
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
        /// Создает счет пользователя, если он еще не существует.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя.</param>
        /// <returns>True, если счет был создан.</returns>
        public async Task<bool> CreateAccountAsync(string userId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                @"INSERT INTO accounts (user_id, balance, created_at)
                  VALUES (@user_id, 0, @created_at)
                  ON CONFLICT (user_id) DO NOTHING;",
                connection);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

            var rows = await command.ExecuteNonQueryAsync();
            return rows == 1;
        }

        /// <summary>
        /// Возвращает баланс пользователя или null, если счет отсутствует.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя.</param>
        public async Task<long?> GetBalanceAsync(string userId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                "SELECT balance FROM accounts WHERE user_id = @user_id;",
                connection);
            command.Parameters.AddWithValue("user_id", userId);

            var result = await command.ExecuteScalarAsync();
            if (result == null || result is DBNull)
            {
                return null;
            }

            return (long)result;
        }

        /// <summary>
        /// Пополняет счет пользователя и фиксирует транзакцию.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя.</param>
        /// <param name="amount">Сумма пополнения.</param>
        /// <returns>True, если счет найден и обновлен.</returns>
        public async Task<bool> TopUpAsync(string userId, long amount)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using var update = new NpgsqlCommand(
                "UPDATE accounts SET balance = balance + @amount WHERE user_id = @user_id;",
                connection,
                transaction);
            update.Parameters.AddWithValue("amount", amount);
            update.Parameters.AddWithValue("user_id", userId);

            var rows = await update.ExecuteNonQueryAsync();
            if (rows == 0)
            {
                await transaction.RollbackAsync();
                return false;
            }

            await using var insert = new NpgsqlCommand(
                @"INSERT INTO account_transactions (id, user_id, amount, reference, created_at)
                  VALUES (@id, @user_id, @amount, @reference, @created_at);",
                connection,
                transaction);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("user_id", userId);
            insert.Parameters.AddWithValue("amount", amount);
            insert.Parameters.AddWithValue("reference", "TopUp");
            insert.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

            await insert.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return true;
        }

        /// <summary>
        /// Идемпотентно обрабатывает запрос на оплату и формирует результат.
        /// </summary>
        /// <param name="request">Запрос на оплату.</param>
        /// <returns>Результат обработки платежа.</returns>
        public async Task<PaymentResultMessage> ProcessPaymentAsync(PaymentRequestMessage request)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var inboxInserted = await TryInsertInboxAsync(connection, transaction, request);
            if (!inboxInserted)
            {
                var existing = await GetPaymentByOrderIdAsync(connection, transaction, request.OrderId);
                if (existing != null)
                {
                    await transaction.CommitAsync();
                    return existing;
                }
            }

            var existingByOrder = await GetPaymentByOrderIdAsync(connection, transaction, request.OrderId);
            if (existingByOrder != null)
            {
                await transaction.CommitAsync();
                return existingByOrder;
            }

            var balance = await GetAccountBalanceForUpdate(connection, transaction, request.UserId);
            var debited = false;
            PaymentStatus status;

            if (balance == null)
            {
                status = PaymentStatus.FailedNoAccount;
            }
            else if (balance < request.Amount)
            {
                status = PaymentStatus.FailedInsufficientFunds;
            }
            else
            {
                debited = await TryDebitAsync(connection, transaction, request.UserId, request.Amount);
                status = debited ? PaymentStatus.Paid : PaymentStatus.FailedInsufficientFunds;
            }

            await InsertPaymentOperationAsync(connection, transaction, request, status);

            if (debited)
            {
                await InsertAccountTransactionAsync(connection, transaction, request.UserId, -request.Amount, request.OrderId);
            }

            var result = new PaymentResultMessage
            {
                MessageId = request.MessageId,
                OrderId = request.OrderId,
                UserId = request.UserId,
                Amount = request.Amount,
                Status = status
            };

            await InsertOutboxAsync(connection, transaction, result);
            await transaction.CommitAsync();
            return result;
        }

        /// <summary>
        /// Возвращает партию неотправленных outbox сообщений.
        /// </summary>
        /// <param name="limit">Максимальное число сообщений.</param>
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

        private static async Task<bool> TryInsertInboxAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PaymentRequestMessage request)
        {
            await using var command = new NpgsqlCommand(
                @"INSERT INTO inbox_messages (message_id, type, payload, received_at)
                  VALUES (@message_id, @type, @payload, @received_at)
                  ON CONFLICT (message_id) DO NOTHING;",
                connection,
                transaction);
            command.Parameters.AddWithValue("message_id", request.MessageId);
            command.Parameters.AddWithValue("type", "PaymentRequested");
            command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(request, JsonOptions);
            command.Parameters.AddWithValue("received_at", DateTimeOffset.UtcNow);

            var rows = await command.ExecuteNonQueryAsync();
            return rows == 1;
        }

        private static async Task<PaymentResultMessage> GetPaymentByOrderIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string orderId)
        {
            await using var command = new NpgsqlCommand(
                "SELECT message_id, user_id, amount, status FROM payment_operations WHERE order_id = @order_id;",
                connection,
                transaction);
            command.Parameters.AddWithValue("order_id", orderId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new PaymentResultMessage
            {
                MessageId = reader.GetString(0),
                OrderId = orderId,
                UserId = reader.GetString(1),
                Amount = reader.GetInt64(2),
                Status = ParseStatus(reader.GetString(3))
            };
        }

        private static async Task<long?> GetAccountBalanceForUpdate(NpgsqlConnection connection, NpgsqlTransaction transaction, string userId)
        {
            await using var command = new NpgsqlCommand(
                "SELECT balance FROM accounts WHERE user_id = @user_id FOR UPDATE;",
                connection,
                transaction);
            command.Parameters.AddWithValue("user_id", userId);

            var result = await command.ExecuteScalarAsync();
            if (result == null || result is DBNull)
            {
                return null;
            }

            return (long)result;
        }

        private static async Task<bool> TryDebitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string userId, long amount)
        {
            await using var command = new NpgsqlCommand(
                @"UPDATE accounts
                  SET balance = balance - @amount
                  WHERE user_id = @user_id AND balance >= @amount;",
                connection,
                transaction);
            command.Parameters.AddWithValue("amount", amount);
            command.Parameters.AddWithValue("user_id", userId);

            var rows = await command.ExecuteNonQueryAsync();
            return rows == 1;
        }

        private static async Task InsertPaymentOperationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PaymentRequestMessage request, PaymentStatus status)
        {
            await using var command = new NpgsqlCommand(
                @"INSERT INTO payment_operations (order_id, message_id, user_id, amount, status, created_at)
                  VALUES (@order_id, @message_id, @user_id, @amount, @status, @created_at);",
                connection,
                transaction);
            command.Parameters.AddWithValue("order_id", request.OrderId);
            command.Parameters.AddWithValue("message_id", request.MessageId);
            command.Parameters.AddWithValue("user_id", request.UserId);
            command.Parameters.AddWithValue("amount", request.Amount);
            command.Parameters.AddWithValue("status", status.ToString());
            command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

            await command.ExecuteNonQueryAsync();
        }

        private static async Task InsertAccountTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string userId, long amount, string reference)
        {
            await using var command = new NpgsqlCommand(
                @"INSERT INTO account_transactions (id, user_id, amount, reference, created_at)
                  VALUES (@id, @user_id, @amount, @reference, @created_at);",
                connection,
                transaction);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("amount", amount);
            command.Parameters.AddWithValue("reference", reference);
            command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

            await command.ExecuteNonQueryAsync();
        }

        private static async Task InsertOutboxAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PaymentResultMessage result)
        {
            await using var command = new NpgsqlCommand(
                @"INSERT INTO outbox_messages (message_id, type, payload, occurred_at)
                  VALUES (@message_id, @type, @payload, @occurred_at);",
                connection,
                transaction);
            command.Parameters.AddWithValue("message_id", result.MessageId);
            command.Parameters.AddWithValue("type", "PaymentResult");
            command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(result, JsonOptions);
            command.Parameters.AddWithValue("occurred_at", DateTimeOffset.UtcNow);

            await command.ExecuteNonQueryAsync();
        }

        private static PaymentStatus ParseStatus(string value)
        {
            if (Enum.TryParse<PaymentStatus>(value, true, out var status))
            {
                return status;
            }

            return PaymentStatus.FailedNoAccount;
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
