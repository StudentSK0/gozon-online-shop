using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gozon.Orders.Api.Background;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gozon.Orders.Api.Realtime
{
    /// <summary>
    /// Получает обновления статусов заказов из RabbitMQ и рассылает по WebSocket.
    /// </summary>
    public class OrderStatusConsumer : BackgroundService
    {
        private readonly OrderStatusSocketManager _socketManager;
        private readonly ILogger<OrderStatusConsumer> _logger;
        private readonly RabbitMqOptions _options;
        private IConnection _connection;
        private IModel _channel;
        private string _queueName = string.Empty;

        /// <summary>
        /// Создает консьюмера статусов заказов.
        /// </summary>
        /// <param name="socketManager">Менеджер WebSocket подключений.</param>
        /// <param name="configuration">Конфигурация приложения.</param>
        /// <param name="logger">Логгер.</param>
        public OrderStatusConsumer(
            OrderStatusSocketManager socketManager,
            IConfiguration configuration,
            ILogger<OrderStatusConsumer> logger)
        {
            _socketManager = socketManager;
            _logger = logger;
            _options = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        }

        /// <summary>
        /// Инициализирует fanout обменник и приватную очередь для инстанса.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        public override Task StartAsync(CancellationToken cancellationToken)
        {
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
            var queue = _channel.QueueDeclare(string.Empty, durable: false, exclusive: true, autoDelete: true);
            _queueName = queue.QueueName;
            _channel.QueueBind(_queueName, _options.OrderStatusExchange, string.Empty);
            _channel.BasicQos(0, 10, false);

            _logger.LogInformation("Order status consumer connected to exchange {Exchange}", _options.OrderStatusExchange);
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Потребляет сообщения статусов и отправляет их WebSocket-клиентам.
        /// </summary>
        /// <param name="stoppingToken">Токен остановки.</param>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_channel == null)
            {
                return Task.CompletedTask;
            }

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (_, ea) =>
            {
                try
                {
                    var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var update = JsonSerializer.Deserialize<OrderStatusUpdate>(body, OrderStatusUpdate.JsonOptions);
                    if (update == null)
                    {
                        _channel.BasicAck(ea.DeliveryTag, false);
                        return;
                    }

                    await _socketManager.BroadcastAsync(update, stoppingToken);
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to broadcast order status update");
                    _channel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _channel.BasicConsume(_queueName, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Закрывает канал и соединение RabbitMQ при завершении.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _channel?.Close();
            _connection?.Close();
            return base.StopAsync(cancellationToken);
        }
    }
}
