namespace Gozon.Orders.Domain.Models
{
    /// <summary>
    /// Определяет состояния жизненного цикла заказа в сервисе Orders.
    /// </summary>
    public enum OrderStatus
    {
        /// <summary>Заказ создан и ожидает оплаты.</summary>
        NEW = 0,
        /// <summary>Оплата прошла успешно, заказ завершён.</summary>
        FINISHED = 1,
        /// <summary>Оплата не удалась, заказ отменён.</summary>
        CANCELLED = 2
    }
}
