using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

/// <summary>The single JSON hash contract (ADR 0091). Frozen with every hashed type from Elsa 4's first stable release.</summary>
public static partial class JsonHashInput
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Returns lowercase SHA-256 hex over exact canonical JSON, independent of its original spelling.</summary>
    public static string Hash(JsonElement value) => Convert.ToHexStringLower(SHA256.HashData(JsonCanonicalForm.GetBytes(value)));

    /// <summary>Serializes an owned contract with explicitly fixed hash settings, then hashes its canonical value.</summary>
    public static string Hash<T>(T value) => Hash(JsonSerializer.SerializeToElement(value, OperationOptions()));

    /// <summary>Serializes with the declared contract type, then hashes the canonical value.</summary>
    public static string Hash(object? value, Type type) => Hash(JsonSerializer.SerializeToElement(value, type, OperationOptions()));

    /// <summary>Parses a JSON document and hashes its canonical value, never its whitespace or escape spelling.</summary>
    public static string HashJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1000 });
        return Hash(document.RootElement);
    }

    /// <summary>Parses UTF-8 JSON emitted by an explicit projection writer, then hashes its canonical value.</summary>
    public static string HashUtf8(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1000 });
        return Hash(document.RootElement);
    }

    /// <summary>Serializes a component of an explicit hash projection with the fixed hash settings.</summary>
    public static JsonElement SerializeToElement<T>(T value) => JsonSerializer.SerializeToElement(value, OperationOptions());
    /// <summary>Serializes a component using an explicitly selected CLR contract and the fixed hash settings.</summary>
    public static JsonElement SerializeToElement(object? value, Type type) => JsonSerializer.SerializeToElement(value, type, OperationOptions());

    /// <summary>Gets canonical UTF-8 bytes for a JSON component of an existing framed or keyed hash protocol.</summary>
    public static byte[] CanonicalBytes(JsonElement value) => JsonCanonicalForm.GetBytes(value);

    /// <summary>Hashes an existing binary frame whose JSON components have been canonicalized through this contract.</summary>
    public static string HashFramed(ReadOnlySpan<byte> frame) => Convert.ToHexStringLower(SHA256.HashData(frame));

    /// <summary>Preserves a secret-bearing revision's keyed integrity while canonicalizing its JSON input.</summary>
    public static byte[] HashKeyed(JsonElement value, ReadOnlySpan<byte> key) => HMACSHA256.HashData(key, JsonCanonicalForm.GetBytes(value));

    // The settings contract is singular and frozen. Metadata belongs to this operation, so an arbitrary
    // collectible contract (also nested in a BCL generic/object slot) cannot be pinned by Core's static options.
    private static JsonSerializerOptions OperationOptions()
    {
        var options = new JsonSerializerOptions(Options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.MakeReadOnly();
        return options;
    }

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
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
        options.MakeReadOnly();
        return options;
    }
}
