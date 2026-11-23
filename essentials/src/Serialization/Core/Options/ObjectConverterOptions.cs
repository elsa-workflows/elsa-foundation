using System.Text.Json;

namespace Elsa.Serialization.Core.Options;

/// <summary>
/// Provides options to the conversion method.
/// </summary>
public record ObjectConverterOptions(
    JsonSerializerOptions? SerializerOptions = null,
    IWellKnownTypeRegistry? WellKnownTypeRegistry = null,
    bool DeserializeJsonObjectToObject = false,
    bool? StrictMode = null
 )
{
    /// <summary>The scoped code-selected contract for a JSON boundary. SerializerOptions does not override this contract.</summary>
    public JsonConversionContract? ConversionContract { get; init; }
    /// <summary>The owning execution, import or response channel records these diagnostics.</summary>
    public Action<JsonConversionDiagnostic>? ReportJsonDiagnostic { get; init; }
}
