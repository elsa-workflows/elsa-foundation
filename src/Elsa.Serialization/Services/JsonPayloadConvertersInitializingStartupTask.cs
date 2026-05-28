using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;

namespace Elsa.Serialization.Services;

/// <summary>
/// Startup task that drives the framework §2.6.1 Registry + StartUp Task sub-pattern for
/// JSON payload converters (Elsa §E3.3 worked example). On execute:
///
/// 1. Publishes <see cref="OnJsonPayloadConvertersInitializing"/> through the mediator.
/// 2. Awaits the full handler chain — each handler contributes converters via
///    <see cref="OnJsonPayloadConvertersInitializing.AddConverter"/>.
/// 3. Flushes the accumulated <see cref="OnJsonPayloadConvertersInitializing.Converters"/>
///    into the singleton <see cref="JsonPayloadConverterRegistry"/>.
///
/// After this task completes, <see cref="JsonPayloadSerializer"/> reads the registry
/// synchronously while building <see cref="System.Text.Json.JsonSerializerOptions"/>.
/// </summary>
public sealed class JsonPayloadConvertersInitializingStartupTask(
    IDomainEventSender domainEventSender,
    JsonPayloadConverterRegistry registry)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var @event = new OnJsonPayloadConvertersInitializing();
        await domainEventSender.Send(@event, cancellationToken);
        registry.RegisterAll(@event.Converters);
    }
}
