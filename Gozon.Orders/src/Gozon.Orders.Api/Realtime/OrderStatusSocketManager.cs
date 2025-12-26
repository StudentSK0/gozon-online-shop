using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Gozon.Orders.Api.Realtime
{
    /// <summary>
    /// Управляет WebSocket-подключениями клиентов и рассылкой статусов заказов.
    /// </summary>
    public class OrderStatusSocketManager
    {
        private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
        private readonly ILogger<OrderStatusSocketManager> _logger;

        /// <summary>
        /// Создает менеджер подключений.
        /// </summary>
        /// <param name="logger">Логгер.</param>
        public OrderStatusSocketManager(ILogger<OrderStatusSocketManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Регистрирует новое WebSocket-подключение.
        /// </summary>
        /// <param name="socket">WebSocket клиента.</param>
        /// <param name="userId">Идентификатор пользователя.</param>
        /// <param name="orderId">Идентификатор заказа (опционально).</param>
        /// <returns>Идентификатор подключения.</returns>
        public string Add(WebSocket socket, string userId, string orderId)
        {
            var connectionId = Guid.NewGuid().ToString("N");
            _connections[connectionId] = new ClientConnection(socket, userId, orderId);
            _logger.LogInformation("WebSocket connected {ConnectionId} for user {UserId}", connectionId, userId);
            return connectionId;
        }

        /// <summary>
        /// Удаляет подключение по идентификатору.
        /// </summary>
        /// <param name="connectionId">Идентификатор подключения.</param>
        public void Remove(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                _logger.LogInformation("WebSocket disconnected {ConnectionId} for user {UserId}", connectionId, connection.UserId);
            }
        }

        /// <summary>
        /// Отправляет обновление конкретному подключению.
        /// </summary>
        /// <param name="connectionId">Идентификатор подключения.</param>
        /// <param name="update">Обновление статуса заказа.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task SendToConnectionAsync(string connectionId, OrderStatusUpdate update, CancellationToken cancellationToken)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                await connection.SendAsync(update, cancellationToken);
            }
        }

        /// <summary>
        /// Рассылает обновление всем подходящим подключениям.
        /// </summary>
        /// <param name="update">Обновление статуса заказа.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task BroadcastAsync(OrderStatusUpdate update, CancellationToken cancellationToken)
        {
            foreach (var pair in _connections)
            {
                var connectionId = pair.Key;
                var connection = pair.Value;
                if (!connection.Matches(update))
                {
                    continue;
                }

                if (connection.Socket.State != WebSocketState.Open)
                {
                    Remove(connectionId);
                    continue;
                }

                await connection.SendAsync(update, cancellationToken);
            }
        }

        /// <summary>
        /// Слушает входящие сообщения клиента и закрывает соединение при завершении.
        /// </summary>
        /// <param name="connectionId">Идентификатор подключения.</param>
        /// <param name="socket">WebSocket клиента.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task ListenAsync(string connectionId, WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket connection {ConnectionId} closed with error", connectionId);
            }
            finally
            {
                Remove(connectionId);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
        }

        private sealed class ClientConnection
        {
            private readonly SemaphoreSlim _sendLock = new(1, 1);

            public ClientConnection(WebSocket socket, string userId, string orderId)
            {
                Socket = socket;
                UserId = userId;
                OrderId = orderId;
            }

            public WebSocket Socket { get; }
            public string UserId { get; }
            public string OrderId { get; }

            public bool Matches(OrderStatusUpdate update)
            {
                if (!string.Equals(UserId, update.UserId, StringComparison.Ordinal))
                {
                    return false;
                }

                return string.IsNullOrWhiteSpace(OrderId) || string.Equals(OrderId, update.OrderId, StringComparison.Ordinal);
            }

            public async Task SendAsync(OrderStatusUpdate update, CancellationToken cancellationToken)
            {
                var payload = JsonSerializer.Serialize(update, OrderStatusUpdate.JsonOptions);
                var buffer = Encoding.UTF8.GetBytes(payload);

                await _sendLock.WaitAsync(cancellationToken);
                try
                {
                    if (Socket.State == WebSocketState.Open)
                    {
                        await Socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
    }
}
