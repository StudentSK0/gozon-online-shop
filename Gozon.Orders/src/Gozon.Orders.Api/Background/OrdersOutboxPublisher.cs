using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gozon.Orders.Domain.Models;
using Gozon.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Gozon.Orders.Api.Background
{
    /// <summary>
    /// Фоновый воркер, публикующий outbox-сообщения заказов в RabbitMQ.
    /// </summary>
    public class OrdersOutboxPublisher : BackgroundService
    {
        private readonly OrdersStore _store;
        private readonly ILogger<OrdersOutboxPublisher> _logger;
        private readonly RabbitMqOptions _options;
        private IConnection _connection;
        private IModel _channel;

        /// <summary>
        /// Создает паблишер с доступом к хранилищу и настройкам RabbitMQ.
        /// </summary>
        /// <param name="store">Хранилище заказов.</param>
        /// <param name="configuration">Конфигурация приложения.</param>
        /// <param name="logger">Логгер.</param>
        public OrdersOutboxPublisher(OrdersStore store, IConfiguration configuration, ILogger<OrdersOutboxPublisher> logger)
        {
            _store = store;
            _logger = logger;
            _options = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        }

        /// <summary>
        /// Инициализирует канал RabbitMQ и объявляет очередь запросов оплаты.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                UserName = _options.UserName,
                Password = _options.Password
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_options.PaymentsRequestQueue, durable: true, exclusive: false, autoDelete: false);

            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Опрашивает outbox и публикует сообщения в очередь запросов оплаты.
        /// </summary>
        /// <param name="stoppingToken">Токен остановки.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var messages = await _store.GetPendingOutboxAsync(10);
                    if (messages.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }

                    foreach (var message in messages)
                    {
                        var payload = JsonSerializer.Deserialize<PaymentRequestMessage>(message.Payload);
                        if (payload == null)
                        {
                            await _store.MarkOutboxProcessedAsync(message.MessageId);
                            continue;
                        }

                        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                        var properties = _channel?.CreateBasicProperties();
                        if (properties != null)
                        {
                            properties.DeliveryMode = 2;
                        }

                        _channel?.BasicPublish(
                            exchange: string.Empty,
                            routingKey: _options.PaymentsRequestQueue,
                            basicProperties: properties,
                            body: body);

                        await _store.MarkOutboxProcessedAsync(message.MessageId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish order payment requests");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
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
