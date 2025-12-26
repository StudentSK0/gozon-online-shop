using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Gozon.Payments.Core.Models;
using Gozon.Payments.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gozon.Payments.Api.Background
{
    /// <summary>
    /// Потребляет запросы на оплату и запускает обработку списания.
    /// </summary>
    public class PaymentRequestConsumer : BackgroundService
    {
        private readonly PaymentsStore _store;
        private readonly ILogger<PaymentRequestConsumer> _logger;
        private readonly RabbitMqOptions _options;
        private IConnection _connection;
        private IModel _channel;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Создает консьюмера запросов оплаты.
        /// </summary>
        /// <param name="store">Хранилище платежей.</param>
        /// <param name="configuration">Конфигурация приложения.</param>
        /// <param name="logger">Логгер.</param>
        public PaymentRequestConsumer(PaymentsStore store, IConfiguration configuration, ILogger<PaymentRequestConsumer> logger)
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
                Password = _options.Password,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_options.PaymentsRequestQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.BasicQos(0, 10, false);

            _logger.LogInformation("Payments consumer connected to queue {Queue}", _options.PaymentsRequestQueue);

            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Обрабатывает входящие запросы и фиксирует результат в хранилище.
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
                    var message = JsonSerializer.Deserialize<PaymentRequestMessage>(body, _jsonOptions);
                    if (message == null)
                    {
                        _channel.BasicAck(ea.DeliveryTag, false);
                        return;
                    }

                    _logger.LogInformation("Processing payment request {MessageId} for order {OrderId}", message.MessageId, message.OrderId);
                    await _store.ProcessPaymentAsync(message);
                    _channel.BasicAck(ea.DeliveryTag, false);
                    _logger.LogInformation("Payment request {MessageId} processed", message.MessageId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process payment request");
                    _channel.BasicNack(ea.DeliveryTag, false, true);
                }
            };

            _channel.BasicConsume(_options.PaymentsRequestQueue, autoAck: false, consumer: consumer);
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
