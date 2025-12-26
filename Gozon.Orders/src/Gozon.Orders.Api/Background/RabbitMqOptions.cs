namespace Gozon.Orders.Api.Background
{
    /// <summary>
    /// Настройки подключения и имена очередей/обменников RabbitMQ.
    /// </summary>
    public class RabbitMqOptions
    {
        /// <summary>Имя хоста RabbitMQ.</summary>
        public string HostName { get; set; } = "localhost";
        /// <summary>Имя пользователя RabbitMQ.</summary>
        public string UserName { get; set; } = "guest";
        /// <summary>Пароль RabbitMQ.</summary>
        public string Password { get; set; } = "guest";
        /// <summary>Очередь для исходящих запросов на оплату.</summary>
        public string PaymentsRequestQueue { get; set; } = "payments.requests";
        /// <summary>Очередь для входящих результатов оплаты.</summary>
        public string PaymentsResultQueue { get; set; } = "payments.results";
        /// <summary>Fanout-обменник для статусов заказов.</summary>
        public string OrderStatusExchange { get; set; } = "orders.status";
    }
}
