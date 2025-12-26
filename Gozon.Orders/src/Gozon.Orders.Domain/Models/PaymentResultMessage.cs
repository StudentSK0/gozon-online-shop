namespace Gozon.Orders.Domain.Models
{
    /// <summary>
    /// Результат попытки оплаты, отправленный Payments Service.
    /// </summary>
    public class PaymentResultMessage
    {
        /// <summary>Идемпотентный идентификатор запроса.</summary>
        public string MessageId { get; set; } = string.Empty;
        /// <summary>Идентификатор оплачиваемого заказа.</summary>
        public string OrderId { get; set; } = string.Empty;
        /// <summary>Идентификатор пользователя-владельца.</summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>Сумма заказа в целых единицах валюты.</summary>
        public long Amount { get; set; }
        /// <summary>Строковый статус оплаты от Payments Service.</summary>
        public string Status { get; set; } = string.Empty;
    }
}
