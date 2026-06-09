using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.JsonConverters;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.SystemText.Sources;

/// <summary>
/// Contributes the built-in <see cref="JsonConverter"/> set shipped by the Serialization
/// feature: enum-as-string, TimeSpan, polymorphic-object factory, Type converter.
/// </summary>
public sealed class BuiltInJsonConverterSource(
    PolymorphicObjectConverterFactory polymorphicObjectConverterFactory,
    TypeJsonConverter typeJsonConverter)
    : IJsonConverterSource
{
    public IEnumerable<JsonConverter> GetConverters()
    {
        yield return new JsonStringEnumConverter();
        yield return JsonMetadataServices.TimeSpanConverter;
        yield return polymorphicObjectConverterFactory;
        yield return typeJsonConverter;
    }
}
