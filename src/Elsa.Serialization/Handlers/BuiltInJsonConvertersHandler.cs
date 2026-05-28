using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.JsonConverters;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Handlers;

/// <summary>
/// Contributes the built-in <see cref="JsonConverter"/> set shipped by the Serialization
/// feature: enum-as-string, TimeSpan, polymorphic-object factory, Type converter. Replaces
/// the legacy <c>IPayloadSerializerConverterProvider</c> registrations (one per converter)
/// with a single handler that calls <c>AddConverter</c> per converter.
/// </summary>
public sealed class BuiltInJsonConvertersHandler(
    PolymorphicObjectConverterFactory polymorphicObjectConverterFactory,
    TypeJsonConverter typeJsonConverter)
    : IDomainEventHandler<OnJsonPayloadConvertersInitializing>
{
    public ValueTask Handle(OnJsonPayloadConvertersInitializing domainEvent, CancellationToken cancellationToken)
    {
        domainEvent.AddConverter(new JsonStringEnumConverter());
        domainEvent.AddConverter(JsonMetadataServices.TimeSpanConverter);
        domainEvent.AddConverter(polymorphicObjectConverterFactory);
        domainEvent.AddConverter(typeJsonConverter);
        return ValueTask.CompletedTask;
    }
}
