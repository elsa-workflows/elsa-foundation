using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

/// <summary>The immutable JSON convention for Elsa-owned contracts (ADR 0091).</summary>
public static class JsonBaseline
{
    /// <summary>Gets the shared, read-only baseline. Workflow JSON is carried without rewriting its text.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>Creates independent mutable options for a module's own metadata and registered deviations.</summary>
    public static JsonSerializerOptions CreateCopy() => new(Options);

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new RawJsonElementConverter());
        options.MakeReadOnly();
        return options;
    }
}
