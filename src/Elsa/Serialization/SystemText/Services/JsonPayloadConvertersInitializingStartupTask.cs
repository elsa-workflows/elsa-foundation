using Elsa.Events.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;

namespace Elsa.Serialization.SystemText.Services;

/// <summary>
/// Startup task that drives the framework §2.6.1 Registry + StartUp Task sub-pattern for
/// JSON payload converters (Elsa §E3.3 worked example). On execute:
///
/// 1. Publishes <see cref="JsonPayloadConvertersInitializing"/> through the mediator.
/// 2. Awaits the single <c>RegisterJsonConverters</c> handler, which resolves every
///    <see cref="IJsonConverterSource"/> and adds their converters to
///    <see cref="JsonPayloadConvertersInitializing.Converters"/>.
/// 3. Flushes the accumulated <see cref="JsonPayloadConvertersInitializing.Converters"/>
///    into the singleton <see cref="JsonPayloadConverterRegistry"/>.
///
/// After this task completes, <see cref="JsonPayloadSerializer"/> reads the registry
/// synchronously while building <see cref="System.Text.Json.JsonSerializerOptions"/>.
/// </summary>
public sealed class JsonPayloadConvertersInitializingStartupTask(
    IInlineEventPublisher eventPublisher,
    JsonPayloadConverterRegistry registry)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var @event = new JsonPayloadConvertersInitializing();
        await eventPublisher.Publish(@event, cancellationToken);
        registry.RegisterAll(@event.Converters);
    }
}
