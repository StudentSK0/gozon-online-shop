namespace Gozon.Orders.Api.Contracts
{
    /// <summary>
    /// Запрос на создание нового заказа.
    /// </summary>
    public class CreateOrderRequest
    {
        /// <summary>Сумма заказа в целых единицах валюты.</summary>
        public long Amount { get; set; }
        /// <summary>Описание заказа.</summary>
        public string Description { get; set; } = string.Empty;
    }
}
