using Elsa.Events.Core.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Serialization.SystemText.Handlers;

/// <summary>
/// The single <c>JsonPayloadConvertersInitializing</c> handler. Resolves every registered
/// <see cref="IJsonConverterSource"/>, collects their converters, and adds them to the event.
/// Replaces the former one-event-handler-per-contributor sprawl; features now contribute by
/// registering an <see cref="IJsonConverterSource"/>.
/// </summary>
public sealed class RegisterJsonConverters(IEnumerable<IJsonConverterSource> sources)
    : IEventHandler<JsonPayloadConvertersInitializing>
{
    public Task Handle(JsonPayloadConvertersInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var converter in source.GetConverters())
                domainEvent.Converters.Add(converter);

        return Task.CompletedTask;
    }
}
