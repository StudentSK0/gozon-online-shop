using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gozon.Orders.Domain.Models;
using Gozon.Orders.Infrastructure.Persistence;
using Gozon.Orders.Api.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gozon.Orders.Api.Background
{
    /// <summary>
    /// Потребляет результаты оплаты и обновляет статусы заказов.
    /// </summary>
    public class PaymentResultConsumer : BackgroundService
    {
        private readonly OrdersStore _store;
        private readonly ILogger<PaymentResultConsumer> _logger;
        private readonly RabbitMqOptions _options;
        private readonly OrderStatusPublisher _publisher;
        private IConnection _connection;
        private IModel _channel;

        /// <summary>
        /// Создает консьюмера с доступом к хранилищу и настройкам RabbitMQ.
        /// </summary>
        /// <param name="store">Хранилище заказов.</param>
        /// <param name="publisher">Паблишер статусов.</param>
        /// <param name="configuration">Конфигурация приложения.</param>
        /// <param name="logger">Логгер.</param>
        public PaymentResultConsumer(
            OrdersStore store,
            OrderStatusPublisher publisher,
            IConfiguration configuration,
            ILogger<PaymentResultConsumer> logger)
        {
            _store = store;
            _publisher = publisher;
            _logger = logger;
            _options = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        }

        /// <summary>
        /// Инициализирует канал RabbitMQ и объявляет очередь результатов оплаты.
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
            _channel.QueueDeclare(_options.PaymentsResultQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.BasicQos(0, 10, false);

            _logger.LogInformation("Orders consumer connected to queue {Queue}", _options.PaymentsResultQueue);

            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Обрабатывает результаты оплаты и обновляет заказы идемпотентно.
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
                    var message = JsonSerializer.Deserialize<PaymentResultMessage>(body);
                    if (message == null)
                    {
                        _channel.BasicAck(ea.DeliveryTag, false);
                        return;
                    }

                    _logger.LogInformation("Applying payment result {MessageId} for order {OrderId}", message.MessageId, message.OrderId);
                    var updated = await _store.ApplyPaymentResultAsync(message);
                    if (updated != null)
                    {
                        _publisher.Publish(OrderStatusUpdate.FromOrder(updated));
                    }
                    _channel.BasicAck(ea.DeliveryTag, false);
                    _logger.LogInformation("Payment result {MessageId} applied", message.MessageId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply payment result");
                    _channel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _channel.BasicConsume(_options.PaymentsResultQueue, autoAck: false, consumer: consumer);
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
