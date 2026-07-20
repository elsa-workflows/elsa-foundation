using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Canonical JSON plus its command-owned request or result fingerprint. Operation identity remains
/// separate: the caller's operation key never becomes part of this material.
/// </summary>
public sealed record GroundworkDesignAtomicWriteMaterial(string Json, string Fingerprint)
{
    private const string FingerprintIdentity = "elsa-design-material:v1";
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    public static GroundworkDesignAtomicWriteMaterial Create<T>(
        string operationKind,
        string materialSchemaVersion,
        T material,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSchemaVersion);
        ArgumentNullException.ThrowIfNull(material);

        try
        {
            var element = JsonSerializer.SerializeToElement(material, jsonOptions ?? DefaultJsonOptions);
            var json = WriteCanonicalJson(element);
            return new GroundworkDesignAtomicWriteMaterial(
                json,
                CreateFingerprint(operationKind, materialSchemaVersion, json));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GroundworkDesignAtomicWriteSerializationException(
                $"The {operationKind} design-operation material could not be serialized.",
                exception);
        }
    }

    public static T Deserialize<T>(
        string authoritativeResultFingerprint,
        string authoritativeResultJson,
        string operationKind,
        string materialSchemaVersion,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritativeResultFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritativeResultJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSchemaVersion);

        try
        {
            using var document = JsonDocument.Parse(authoritativeResultJson);
            var canonicalJson = WriteCanonicalJson(document.RootElement);
            var expectedFingerprint = CreateFingerprint(
                operationKind,
                materialSchemaVersion,
                canonicalJson);
            if (!StringComparer.Ordinal.Equals(expectedFingerprint, authoritativeResultFingerprint))
            {
                throw new GroundworkDesignAtomicWriteCorruptResultException(
                    $"The authoritative result for design operation '{operationKind}' does not match its fingerprint.");
            }

            return JsonSerializer.Deserialize<T>(
                       authoritativeResultJson,
                       jsonOptions ?? DefaultJsonOptions)
                   ?? throw new GroundworkDesignAtomicWriteCorruptResultException(
                       $"The authoritative result for design operation '{operationKind}' deserialized to null.");
        }
        catch (GroundworkDesignAtomicWriteCorruptResultException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GroundworkDesignAtomicWriteCorruptResultException(
                $"The authoritative result for design operation '{operationKind}' could not be deserialized.",
                exception);
        }
    }

    private static string CreateFingerprint(
        string operationKind,
        string materialSchemaVersion,
        string canonicalJson)
    {
        var framed = string.Concat(
            Frame(FingerprintIdentity),
            Frame(operationKind),
            Frame(materialSchemaVersion),
            Frame(canonicalJson));
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)))}";
    }

    private static string Frame(string value) =>
        string.Concat(
            Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture),
            ":",
            value);

    private static string WriteCanonicalJson(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonicalJson(writer, element);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public sealed class GroundworkDesignAtomicWriteCorruptResultException : InvalidOperationException
{
    public GroundworkDesignAtomicWriteCorruptResultException(string message)
        : base(message)
    {
    }

    public GroundworkDesignAtomicWriteCorruptResultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Classifies canonical operation-material serialization failures before they cross a design adapter.</summary>
public sealed class GroundworkDesignAtomicWriteSerializationException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
