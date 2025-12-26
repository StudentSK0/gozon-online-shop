namespace Gozon.Payments.Core.Models
{
    /// <summary>
    /// Итог статуса платежа в Payments Service.
    /// </summary>
    public enum PaymentStatus
    {
        /// <summary>Платеж успешно выполнен.</summary>
        Paid = 0,
        /// <summary>Недостаточно средств на счете.</summary>
        FailedInsufficientFunds = 1,
        /// <summary>Счет пользователя не найден.</summary>
        FailedNoAccount = 2
    }
}
