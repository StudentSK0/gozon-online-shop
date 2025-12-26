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

namespace Gozon.Payments.Api.Background
{
    /// <summary>
    /// Фоновый воркер, публикующий результаты оплат из outbox в RabbitMQ.
    /// </summary>
    public class PaymentsOutboxPublisher : BackgroundService
    {
        private readonly PaymentsStore _store;
        private readonly ILogger<PaymentsOutboxPublisher> _logger;
        private readonly RabbitMqOptions _options;
        private IConnection _connection;
        private IModel _channel;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Создает паблишер результатов оплат.
        /// </summary>
        /// <param name="store">Хранилище платежей.</param>
        /// <param name="configuration">Конфигурация приложения.</param>
        /// <param name="logger">Логгер.</param>
        public PaymentsOutboxPublisher(PaymentsStore store, IConfiguration configuration, ILogger<PaymentsOutboxPublisher> logger)
        {
            _store = store;
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
                Password = _options.Password
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_options.PaymentsResultQueue, durable: true, exclusive: false, autoDelete: false);

            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Отправляет накопленные результаты оплат в очередь.
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
                        var payload = JsonSerializer.Deserialize<PaymentResultMessage>(message.Payload, _jsonOptions);
                        if (payload == null)
                        {
                            await _store.MarkOutboxProcessedAsync(message.MessageId);
                            continue;
                        }

                        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _jsonOptions));
                        var properties = _channel?.CreateBasicProperties();
                        if (properties != null)
                        {
                            properties.DeliveryMode = 2;
                        }

                        _channel?.BasicPublish(
                            exchange: string.Empty,
                            routingKey: _options.PaymentsResultQueue,
                            basicProperties: properties,
                            body: body);

                        await _store.MarkOutboxProcessedAsync(message.MessageId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish payment results");
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
