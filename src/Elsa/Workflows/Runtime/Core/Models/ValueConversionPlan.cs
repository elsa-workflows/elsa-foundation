using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The conversion behavior requested by an authored binding.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueConversionMode>))]
public enum ValueConversionMode
{
    Auto,
    None,
    Json,
    Xml,
    Profile
}

/// <summary>The conversion operation resolved and pinned by publication.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueConversionOperation>))]
public enum ValueConversionOperation
{
    Identity,
    NullableCompatibility,
    NumericWidening,
    RecursiveCollection,
    CanonicalAny,
    Profile
}

/// <summary>Stable identity of a conversion profile selected at publication.</summary>
public sealed record ValueConversionProfileReference
{
    public ValueConversionProfileReference(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Id = id;
        Version = version;
    }

    public string Id { get; }
    public string Version { get; }
}

/// <summary>Safety limits pinned with a conversion plan rather than read from mutable runtime configuration.</summary>
public sealed record ValueConversionLimits
{
    public static ValueConversionLimits Default { get; } = new(64, 10_000, 1_048_576);

    public ValueConversionLimits(int maxDepth, int maxCollectionItems, long maxPayloadBytes)
    {
        if (maxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        if (maxCollectionItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCollectionItems));
        if (maxPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        MaxDepth = maxDepth;
        MaxCollectionItems = maxCollectionItems;
        MaxPayloadBytes = maxPayloadBytes;
    }

    public int MaxDepth { get; }
    public int MaxCollectionItems { get; }
    public long MaxPayloadBytes { get; }
}

/// <summary>
/// Versioned publication result describing the only conversion behavior runtime may apply for a binding.
/// </summary>
public sealed record ValueConversionPlan
{
    public const int CurrentSchemaVersion = 1;

    [JsonConstructor]
    public ValueConversionPlan(
        int schemaVersion,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor sourceType,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionOperation operation,
        ValueConversionProfileReference? profile,
        ValueConversionLimits? limits,
        JsonElement? options,
        string? fingerprint = null)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, $"Unsupported value conversion plan schema version '{schemaVersion}'.");
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);
        if (!Enum.IsDefined(sourceRepresentation))
            throw new ArgumentOutOfRangeException(nameof(sourceRepresentation));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (mode == ValueConversionMode.None && operation != ValueConversionOperation.Identity)
            throw new ArgumentException("The None conversion mode only permits the identity operation.", nameof(operation));
        if ((mode is ValueConversionMode.Json or ValueConversionMode.Xml or ValueConversionMode.Profile) &&
            operation != ValueConversionOperation.Profile)
            throw new ArgumentException("Explicit JSON, XML, and named-profile modes require a profile operation.", nameof(operation));
        if (operation == ValueConversionOperation.Profile && profile is null)
            throw new ArgumentException("A profile conversion operation requires a pinned profile identity.", nameof(profile));
        if (operation != ValueConversionOperation.Profile && profile is not null)
            throw new ArgumentException("Only a profile conversion operation may pin a profile identity.", nameof(profile));

        JsonElement pinnedOptions = options?.Clone() ?? JsonSerializer.SerializeToElement(new { });
        if (pinnedOptions.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Value conversion plan options must be a JSON object.", nameof(options));

        SchemaVersion = schemaVersion;
        SourceRepresentation = sourceRepresentation;
        SourceType = sourceType;
        TargetType = targetType;
        Mode = mode;
        Operation = operation;
        Profile = profile;
        Limits = limits ?? ValueConversionLimits.Default;
        Options = pinnedOptions;

        var computedFingerprint = ComputeFingerprint(this);
        if (fingerprint is not null && !StringComparer.Ordinal.Equals(fingerprint, computedFingerprint))
            throw new ArgumentException("Value conversion plan fingerprint does not match its pinned contents.", nameof(fingerprint));
        Fingerprint = computedFingerprint;
    }

    public int SchemaVersion { get; }
    public ValueRepresentation SourceRepresentation { get; }
    public ValueTypeDescriptor SourceType { get; }
    public ValueTypeDescriptor TargetType { get; }
    public ValueConversionMode Mode { get; }
    public ValueConversionOperation Operation { get; }
    public ValueConversionProfileReference? Profile { get; }
    public ValueConversionLimits Limits { get; }
    public JsonElement? Options { get; }
    public string Fingerprint { get; }

    public static ValueConversionPlan Identity(
        ValueTypeDescriptor type,
        ValueRepresentation representation,
        ValueConversionMode mode = ValueConversionMode.Auto)
    {
        if (mode is not (ValueConversionMode.Auto or ValueConversionMode.None))
            throw new ArgumentOutOfRangeException(nameof(mode), "Identity plans are only valid for Auto and None modes.");

        return new(
            CurrentSchemaVersion,
            representation,
            type,
            type,
            mode,
            ValueConversionOperation.Identity,
            profile: null,
            limits: ValueConversionLimits.Default,
            options: null);
    }

    private static string ComputeFingerprint(ValueConversionPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", plan.SchemaVersion);
            writer.WriteString("sourceRepresentation", plan.SourceRepresentation.ToString());
            WriteType(writer, "sourceType", plan.SourceType);
            WriteType(writer, "targetType", plan.TargetType);
            writer.WriteString("mode", plan.Mode.ToString());
            writer.WriteString("operation", plan.Operation.ToString());
            if (plan.Profile is null)
                writer.WriteNull("profile");
            else
            {
                writer.WriteStartObject("profile");
                writer.WriteString("id", plan.Profile.Id);
                writer.WriteString("version", plan.Profile.Version);
                writer.WriteEndObject();
            }

            writer.WriteStartObject("limits");
            writer.WriteNumber("maxDepth", plan.Limits.MaxDepth);
            writer.WriteNumber("maxCollectionItems", plan.Limits.MaxCollectionItems);
            writer.WriteNumber("maxPayloadBytes", plan.Limits.MaxPayloadBytes);
            writer.WriteEndObject();
            writer.WritePropertyName("options");
            WriteCanonical(writer, plan.Options!.Value);
            writer.WriteEndObject();
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }

    private static void WriteType(Utf8JsonWriter writer, string propertyName, ValueTypeDescriptor type)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("alias", type.Alias);
        writer.WriteString("collectionKind", type.CollectionKind.ToString());
        if (type.SchemaVersion.HasValue)
            writer.WriteNumber("schemaVersion", type.SchemaVersion.Value);
        else
            writer.WriteNull("schemaVersion");
        writer.WritePropertyName("schema");
        if (type.Schema.HasValue)
            WriteCanonical(writer, type.Schema.Value);
        else
            writer.WriteNullValue();
        writer.WriteEndObject();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
