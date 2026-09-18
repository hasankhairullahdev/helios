using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace TaskService.Messaging;

// RabbitMqTaskEventPublisher bertanggung jawab publish event "TaskCreated"
// ke RabbitMQ exchange. Dia implement ITaskEventPublisher supaya bisa di-swap
// dengan mock saat testing.
public class RabbitMqTaskEventPublisher : ITaskEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private const string ExchangeName = "task-events";

    // Constructor private — gunakan factory method CreateAsync() karena
    // koneksi RabbitMQ bersifat async.
    private RabbitMqTaskEventPublisher(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    // Factory method async — pattern ini lebih bersih daripada async di constructor
    public static async Task<RabbitMqTaskEventPublisher> CreateAsync(string hostName)
    {
        var factory = new ConnectionFactory { HostName = hostName };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // Declare exchange type "fanout" — semua subscriber yang bind ke exchange
        // ini akan menerima semua pesan. Alternatifnya "direct" (routing key) atau
        // "topic" (pattern matching), tapi fanout cukup untuk demo ini.
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false
        );

        return new RabbitMqTaskEventPublisher(connection, channel);
    }

    public async Task PublishTaskCreatedAsync(int taskId, string title)
    {
        var eventPayload = new { EventType = "TaskCreated", TaskId = taskId, Title = title, Timestamp = DateTime.UtcNow };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(eventPayload));

        // Publish ke exchange, bukan langsung ke queue.
        // Exchange yang akan route-kan ke semua queue yang terdaftar.
        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: string.Empty, // fanout mengabaikan routing key
            body: body
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
