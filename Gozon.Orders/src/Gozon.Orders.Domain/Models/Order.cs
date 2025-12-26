using System;

namespace Gozon.Orders.Domain.Models
{
    /// <summary>
    /// Агрегат заказа и его статуса оплаты.
    /// </summary>
    public class Order
    {
        /// <summary>Идентификатор заказа.</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Владелец заказа из заголовка X-User-Id.</summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>Сумма заказа в целых единицах валюты.</summary>
        public long Amount { get; set; }
        /// <summary>Описание заказа для отображения пользователю.</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Текущее состояние заказа.</summary>
        public OrderStatus Status { get; set; }
        /// <summary>Время создания (UTC).</summary>
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>Время последнего изменения (UTC).</summary>
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
