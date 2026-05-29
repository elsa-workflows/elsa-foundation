using CShells.Features;
using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.DomainEvents;
using Elsa.Mediator.Notifications.Channels;
using Elsa.Mediator.Notifications.Services;
using Elsa.Mediator.Notifications.Strategies;
using Elsa.Mediator.Requests;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator;

[ShellFeature(
    name: "Mediator"
)]
public class MediatorFeature : IShellFeature
{
    /// <summary>
    /// Default strategy applied when an <see cref="INotificationSender.SendAsync"/> call
    /// doesn't pass an explicit strategy. <see cref="ILifecycleEventSender"/> ignores this
    /// and uses <see cref="NotificationStrategy.Background"/> by design.
    /// </summary>
    public IEventPublishingStrategy DefaultNotificationStrategy { get; set; } = NotificationStrategy.Sequential;

    public void ConfigureServices(IServiceCollection services)
    {
        // Requests + commands + domain events — synchronous, awaited, contribution-style.
        services
            .AddScoped<IRequestSender, RequestSender>()
            .AddScoped<ICommandSender, CommandSender>()
            .AddScoped<IDomainEventSender, DomainEventSender>()
            .AddSingleton<IRequestPipeline, RequestPipeline>()
            .AddSingleton<ICommandPipeline, CommandPipeline>()
            .AddSingleton<IDomainEventPipeline, DomainEventPipeline>();

        // Notifications + lifecycle events — strategy-pluggable, "something happened" semantics.
        services
            .AddSingleton(DefaultNotificationStrategy)
            .AddSingleton<INotificationsChannel, NotificationsChannel>()
            .AddSingleton<INotificationPipeline, NotificationPipeline>()
            .AddScoped<INotificationSender, NotificationSender>()
            .AddScoped<ILifecycleEventSender, LifecycleEventSender>();

        // Background dispatcher for notifications enqueued via NotificationStrategy.Background.
        services.AddSingleton<IBackgroundTask, BackgroundEventPublisher>();
    }
}
