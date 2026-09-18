using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService.Messaging;

// RabbitMqConsumer adalah IHostedService — dia jalan di background
// bersamaan dengan HTTP server. Saat app start, dia connect ke RabbitMQ
// dan mulai listen. Saat app stop, dia gracefully disconnect.
public class RabbitMqConsumer : BackgroundService
{
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly string _rabbitHost;
    private IConnection? _connection;
    private IChannel? _channel;
    private const string ExchangeName = "task-events";
    private const string QueueName = "notification-service";

    public RabbitMqConsumer(ILogger<RabbitMqConsumer> logger, IConfiguration config)
    {
        _logger = logger;
        _rabbitHost = config["RABBITMQ_HOST"] ?? "localhost";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry loop — kalau RabbitMQ belum siap saat service start (race condition
        // di docker-compose), kita tunggu dan coba lagi. Ini penting untuk
        // container orchestration dimana startup order tidak selalu terjamin.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ connection failed. Retrying in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { HostName = _rabbitHost };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Bind ke exchange yang sama dengan yang di-declare oleh task-service.
        // Queue "notification-service" exclusive ke consumer ini.
        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken
        );

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken
        );

        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: string.Empty,
            cancellationToken: stoppingToken
        );

        _logger.LogInformation("Connected to RabbitMQ at {Host}, listening on queue {Queue}", _rabbitHost, QueueName);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());

            try
            {
                var payload = JsonSerializer.Deserialize<TaskCreatedEvent>(body);
                if (payload is not null)
                {
                    // Di production nyata ini bisa kirim email/push notification.
                    // Di sini kita log sebagai simulasi — cukup untuk demo pipeline.
                    _logger.LogInformation(
                        "📬 Notification: Task {TaskId} '{Title}' was created at {Timestamp}",
                        payload.TaskId, payload.Title, payload.Timestamp
                    );
                }

                // Acknowledge — kasih tau RabbitMQ bahwa pesan berhasil diproses.
                // Kalau kita tidak ack, pesan akan di-requeue dan dikirim ulang.
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message: {Body}", body);
                // Negative ack — requeue: false artinya pesan dibuang, bukan di-loop.
                // Di production, ini bisa dikirim ke dead letter queue.
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await _channel.BasicConsumeAsync(queue: QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Tunggu sampai cancellation token dicancel (app shutdown)
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    // Record untuk deserialize payload dari RabbitMQ
    private record TaskCreatedEvent(string EventType, int TaskId, string Title, DateTime Timestamp);
}
