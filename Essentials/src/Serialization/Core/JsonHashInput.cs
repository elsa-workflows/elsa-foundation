using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

/// <summary>The single JSON hash contract (ADR 0091). Frozen with every hashed type from Elsa 4's first stable release.</summary>
public static class JsonHashInput
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Returns lowercase SHA-256 hex over exact canonical JSON, independent of its original spelling.</summary>
    public static string Hash(JsonElement value) => Convert.ToHexStringLower(SHA256.HashData(JsonCanonicalForm.GetBytes(value)));

    /// <summary>Serializes an owned contract with explicitly fixed hash settings, then hashes its canonical value.</summary>
    public static string Hash<T>(T value) => Hash(JsonSerializer.SerializeToElement(value, Options));

    /// <summary>Serializes with the declared contract type, then hashes the canonical value.</summary>
    public static string Hash(object? value, Type type) => Hash(JsonSerializer.SerializeToElement(value, type, Options));

    private static JsonSerializerOptions CreateOptions()
    {
        // Deliberately independent of JsonBaseline, Web and framework Default option instances.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict,
            Encoder = JavaScriptEncoder.Default,
            MaxDepth = 1000,
            WriteIndented = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            IncludeFields = false,
            IgnoreReadOnlyFields = false,
            IgnoreReadOnlyProperties = false,
            ReferenceHandler = null,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
}
