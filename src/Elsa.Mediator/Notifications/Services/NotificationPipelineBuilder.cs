using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Notifications.Services;

/// <inheritdoc />
public sealed class NotificationPipelineBuilder : INotificationPipelineBuilder
{
    private readonly List<Func<NotificationMiddlewareDelegate, NotificationMiddlewareDelegate>> _components = [];

    public NotificationPipelineBuilder(IServiceProvider serviceProvider)
    {
        ApplicationServices = serviceProvider;
    }

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    public IServiceProvider ApplicationServices { get; }

    public INotificationPipelineBuilder Use(Func<NotificationMiddlewareDelegate, NotificationMiddlewareDelegate> middleware)
    {
        _components.Add(middleware);
        return this;
    }

    public NotificationMiddlewareDelegate Build()
    {
        NotificationMiddlewareDelegate pipeline = _ => new ValueTask();

        for (var i = _components.Count - 1; i >= 0; i--)
            pipeline = _components[i](pipeline);

        return pipeline;
    }
}
