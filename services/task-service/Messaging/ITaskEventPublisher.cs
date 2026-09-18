namespace TaskService.Messaging;

// Interface ini penting untuk testability — di unit test kita bisa mock ini
// tanpa perlu RabbitMQ yang beneran jalan.
public interface ITaskEventPublisher
{
    Task PublishTaskCreatedAsync(int taskId, string title);
}
