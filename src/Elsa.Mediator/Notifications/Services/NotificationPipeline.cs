using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Middleware;

namespace Elsa.Mediator.Notifications.Services;

/// <inheritdoc />
public sealed class NotificationPipeline(IServiceProvider serviceProvider) : INotificationPipeline
{
    private NotificationMiddlewareDelegate? _pipeline;

    public NotificationMiddlewareDelegate Pipeline => _pipeline ??= CreateDefaultPipeline();

    public NotificationMiddlewareDelegate Setup(Action<INotificationPipelineBuilder>? setup = null)
    {
        var builder = new NotificationPipelineBuilder(serviceProvider);
        setup?.Invoke(builder);
        _pipeline = builder.Build();
        return _pipeline;
    }

    public Task ExecuteAsync(INotificationContext context) => Pipeline(context).AsTask();

    private NotificationMiddlewareDelegate CreateDefaultPipeline() => Setup(builder =>
    {
        builder.UseMiddleware<NotificationHandlerInvokerMiddleware>();
    });
}
