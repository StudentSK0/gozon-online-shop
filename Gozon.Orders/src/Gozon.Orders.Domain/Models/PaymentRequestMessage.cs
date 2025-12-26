namespace Gozon.Orders.Domain.Models
{
    /// <summary>
    /// Сообщение Orders Service с запросом на оплату заказа.
    /// </summary>
    public class PaymentRequestMessage
    {
        /// <summary>Идемпотентный идентификатор запроса.</summary>
        public string MessageId { get; set; } = string.Empty;
        /// <summary>Идентификатор оплачиваемого заказа.</summary>
        public string OrderId { get; set; } = string.Empty;
        /// <summary>Идентификатор пользователя-владельца.</summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>Сумма заказа в целых единицах валюты.</summary>
        public long Amount { get; set; }
    }
}
