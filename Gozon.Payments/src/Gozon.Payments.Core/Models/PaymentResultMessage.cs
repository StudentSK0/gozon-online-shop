namespace Gozon.Payments.Core.Models
{
    /// <summary>
    /// Результат обработки платежа, отправляемый в Orders Service.
    /// </summary>
    public class PaymentResultMessage
    {
        /// <summary>Идемпотентный идентификатор запроса.</summary>
        public string MessageId { get; set; } = string.Empty;
        /// <summary>Идентификатор заказа.</summary>
        public string OrderId { get; set; } = string.Empty;
        /// <summary>Идентификатор пользователя.</summary>
        public string UserId { get; set; } = string.Empty;
        /// <summary>Сумма в целых единицах валюты.</summary>
        public long Amount { get; set; }
        /// <summary>Итоговый статус обработки платежа.</summary>
        public PaymentStatus Status { get; set; }
    }
}
