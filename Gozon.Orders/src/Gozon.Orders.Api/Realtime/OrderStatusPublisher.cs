using System.Text;
using System.Text.Json;
using Gozon.Orders.Api.Background;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Gozon.Orders.Api.Realtime
{
    /// <summary>
    /// Публикует уведомления о смене статусов заказов в RabbitMQ.
    /// </summary>
    public class OrderStatusPublisher : IDisposable
    {
        private readonly ILogger<OrderStatusPublisher> _logger;
        private readonly RabbitMqOptions _options;
        private readonly object _sync = new();
        private readonly IConnection _connection;
        private readonly IModel _channel;

        /// <summary>
        /// Создает паблишер статусов заказов.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения.</param>
        /// <param name="logger">Логгер.</param>
        public OrderStatusPublisher(IConfiguration configuration, ILogger<OrderStatusPublisher> logger)
        {
            _logger = logger;
            _options = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                UserName = _options.UserName,
                Password = _options.Password,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_options.OrderStatusExchange, ExchangeType.Fanout, durable: true, autoDelete: false);
        }

        /// <summary>
        /// Публикует уведомление о статусе заказа.
        /// </summary>
        /// <param name="update">Данные уведомления.</param>
        public void Publish(OrderStatusUpdate update)
        {
            var payload = JsonSerializer.Serialize(update, OrderStatusUpdate.JsonOptions);
            var body = Encoding.UTF8.GetBytes(payload);

            var properties = _channel.CreateBasicProperties();
            properties.ContentType = "application/json";
            properties.DeliveryMode = 2;

            lock (_sync)
            {
                _channel.BasicPublish(_options.OrderStatusExchange, string.Empty, properties, body);
            }
            _logger.LogInformation("Published order status update for order {OrderId}", update.OrderId);
        }

        /// <summary>
        /// Закрывает канал и соединение RabbitMQ.
        /// </summary>
        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
        }
    }
}
