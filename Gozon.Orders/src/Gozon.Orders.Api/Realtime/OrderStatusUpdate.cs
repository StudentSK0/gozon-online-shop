using System;
using System.Text.Json;
using Gozon.Orders.Domain.Models;

namespace Gozon.Orders.Api.Realtime
{
    /// <summary>
    /// DTO уведомления о смене статуса заказа для WebSocket клиентов.
    /// </summary>
    public class OrderStatusUpdate
    {
        /// <summary>
        /// Настройки сериализации для обмена с фронтендом.
        /// </summary>
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public string Type { get; set; } = "OrderStatusChanged";
        public string OrderId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// Создает уведомление по данным заказа.
        /// </summary>
        /// <param name="order">Заказ-источник.</param>
        /// <returns>Уведомление о статусе заказа.</returns>
        public static OrderStatusUpdate FromOrder(Order order)
        {
            return new OrderStatusUpdate
            {
                OrderId = order.Id,
                UserId = order.UserId,
                Status = order.Status.ToString(),
                Amount = order.Amount,
                Description = order.Description,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt
            };
        }
    }
}
