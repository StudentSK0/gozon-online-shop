namespace Gozon.Payments.Api.Background
{
    /// <summary>
    /// Настройки подключения и имена очередей RabbitMQ.
    /// </summary>
    public class RabbitMqOptions
    {
        /// <summary>Имя хоста RabbitMQ.</summary>
        public string HostName { get; set; } = "localhost";
        /// <summary>Имя пользователя RabbitMQ.</summary>
        public string UserName { get; set; } = "guest";
        /// <summary>Пароль RabbitMQ.</summary>
        public string Password { get; set; } = "guest";
        /// <summary>Очередь запросов оплаты.</summary>
        public string PaymentsRequestQueue { get; set; } = "payments.requests";
        /// <summary>Очередь результатов оплаты.</summary>
        public string PaymentsResultQueue { get; set; } = "payments.results";
    }
}
