using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Renders an enum the way the HTTP API renders it, so a fragment never publishes a spelling the server
/// would not accept.
/// </summary>
/// <remarks>
/// <para>
/// A published <c>collectionKind</c> of <c>"Single"</c> while the submit schema enumerated <c>"single"</c>
/// meant a consumer copying the value straight out of a fragment into a variable declaration got a body the
/// schema rejects. <c>editingMode</c> had the same defect. Both came from calling <c>.ToString()</c> on a
/// CLR enum, which yields the CLR name rather than the wire name.
/// </para>
/// <para>
/// The fix serializes through the same converter configuration the API is configured with
/// (<c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c>, see
/// <c>SerializationFastEndpointConfigurator</c>) rather than lower-casing by hand, so the two cannot drift
/// apart again. <c>ContractCasingTests</c> pins the result against the submit schema's own enumerations.
/// </para>
/// <para>
/// Not every published enum needs this: a field the served view already exposes as a <c>string</c> (like
/// <c>executionType</c>) is delivered verbatim, so its CLR name IS its wire name.
/// </para>
/// </remarks>
public static class WireSpelling
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, Options).Trim('"');
}
