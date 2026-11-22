using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Services;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<IEventChannel, EventChannel>();
        services.TryAddSingleton<IEventPipeline, EventPipeline>();
        services.TryAddScoped<IEventPublisher, EventPublisher>();
        // Activity runtime also composes this canonical path for library hosts. Repeated feature composition
        // preserves the selected publisher/pipeline and any configured middleware.
        services.TryAddScoped<IInlineEventPublisher, InlineEventPublisher>();
        services.TryAddScoped<IDeferredEventPublisher, DeferredEventPublisher>();

        // Background dispatcher for events published via EventPublishingStrategy.Background.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackgroundTask, BackgroundEventPublisher>());
    }
}
