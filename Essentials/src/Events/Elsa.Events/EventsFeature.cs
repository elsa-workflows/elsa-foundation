using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Services;
using Elsa.Events.Strategies;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Events;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Events")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "Events",
    DisplayName = "Events",
    Description = "Provides event publishing, event pipeline dispatch, and background event processing services."
)]
public class EventsFeature : IShellFeature
{
    /// <summary>
    /// Default strategy applied when an <see cref="IEventPublisher.Publish"/> call doesn't pass
    /// an explicit strategy. Sequential by design: synchronous, awaited, fail-fast. The inline and
    /// deferred publishers always pass their own strategy, so only a direct publish uses this.
    /// </summary>
    internal IEventPublishingStrategy DefaultEventPublishingStrategy { get; set; } = EventPublishingStrategy.Sequential;

    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddSingleton(DefaultEventPublishingStrategy)
            .AddSingleton<IEventChannel, EventChannel>()
            .AddSingleton<IEventPipeline, EventPipeline>()
            .AddScoped<IEventPublisher, EventPublisher>()
            // Intent-revealing delivery faces over the strategy-selecting IEventPublisher: inline
            // awaits handler effects (the read-back sites); deferred is fire-and-forget (notifications).
            .AddScoped<IInlineEventPublisher, InlineEventPublisher>()
            .AddScoped<IDeferredEventPublisher, DeferredEventPublisher>();

        // Background dispatcher for events published via EventPublishingStrategy.Background.
        services.AddSingleton<IBackgroundTask, BackgroundEventPublisher>();
    }
}
