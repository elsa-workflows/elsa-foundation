using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Etl.Core;

/// <summary>Reads, writes, and hashes the typed v1 ETL pipeline JSON representation.</summary>
/// <remarks>
/// Write uses the declared typed-property order and compact camel-case JSON. Hash hashes that compact
/// serialization; it is not a general JSON canonicalization or property-sorting algorithm. Design and runtime
/// share this codec so a published artifact has one mechanical representation.
/// </remarks>
public static class EtlArtifactJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a typed pipeline using the v1 camel-case representation.</summary>
    public static JsonElement Write(EtlPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        return JsonSerializer.SerializeToElement(pipeline, Options).Clone();
    }

    /// <summary>Deserializes one pipeline document using strict camel-case property and enum names.</summary>
    /// <exception cref="JsonException">The document is not a pipeline document in the v1 representation.</exception>
    public static EtlPipeline Read(JsonElement document) =>
        document.Deserialize<EtlPipeline>(Options)
        ?? throw new JsonException("The ETL pipeline document resolved to null.");

    /// <summary>Returns lowercase SHA-256 for the compact JSON serialization of a pipeline document.</summary>
    /// <remarks>
    /// Pass a document produced by <see cref="Write(EtlPipeline)"/> for the published typed property order.
    /// This method does not reorder arbitrary JSON object members.
    /// </remarks>
    public static string Hash(JsonElement document) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document, Options))).ToLowerInvariant();

    /// <summary>Builds the versioned, length-framed artifact identity used by publication and runtime readers.</summary>
    public static string CreateArtifactId(int formatVersion, string definitionId, int definitionVersion, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (definitionId.Length > 256 || contentHash.Length != 64 || contentHash.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("The ETL artifact identity components are invalid.");
        var definitionBytes = Encoding.UTF8.GetBytes(definitionId);
        var hashBytes = Encoding.ASCII.GetBytes(contentHash);
        var identity = new byte[sizeof(int) + sizeof(int) + definitionBytes.Length + sizeof(int) + hashBytes.Length];
        var offset = 0;
        BinaryPrimitives.WriteInt32BigEndian(identity.AsSpan(offset, sizeof(int)), formatVersion);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32BigEndian(identity.AsSpan(offset, sizeof(int)), definitionBytes.Length);
        offset += sizeof(int);
        definitionBytes.CopyTo(identity.AsSpan(offset));
        offset += definitionBytes.Length;
        BinaryPrimitives.WriteInt32BigEndian(identity.AsSpan(offset, sizeof(int)), definitionVersion);
        offset += sizeof(int);
        hashBytes.CopyTo(identity.AsSpan(offset));
        return $"etl-v{formatVersion}-{Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant()}";
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
