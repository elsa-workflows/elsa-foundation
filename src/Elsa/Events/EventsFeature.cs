using CShells.Features;
using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Services;
using Elsa.Events.Strategies;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Events;

[ShellFeature(
    name: "Events"
)]
public class EventsFeature : IShellFeature
{
    /// <summary>
    /// Default strategy applied when an <see cref="IEventPublisher.Publish"/> call doesn't pass
    /// an explicit strategy. Sequential by design: synchronous, awaited, fail-fast. Override to
    /// change the default; pass a strategy per-call to override a single publish.
    /// </summary>
    public IEventPublishingStrategy DefaultEventPublishingStrategy { get; set; } = EventPublishingStrategy.Sequential;

    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddSingleton(DefaultEventPublishingStrategy)
            .AddSingleton<IEventChannel, EventChannel>()
            .AddSingleton<IEventPipeline, EventPipeline>()
            .AddScoped<IEventPublisher, EventPublisher>();

        // Background dispatcher for events published via EventPublishingStrategy.Background.
        services.AddSingleton<IBackgroundTask, BackgroundEventPublisher>();
    }
}
