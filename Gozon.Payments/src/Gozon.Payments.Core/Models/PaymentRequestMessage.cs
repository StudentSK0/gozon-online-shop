namespace Gozon.Payments.Core.Models
{
    /// <summary>
    /// Запрос на оплату заказа, полученный из Orders Service.
    /// </summary>
    public class PaymentRequestMessage
    {
        /// <summary>Идемпотентный идентификатор запроса.</summary>
        public string MessageId { get; set; } = string.Empty;
        /// <summary>Идентификатор заказа.</summary>
        public string OrderId { get; set; } = string.Empty;
        /// <summary>Идентификатор пользователя.</summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>Сумма в целых единицах валюты.</summary>
        public long Amount { get; set; }
    }
}
