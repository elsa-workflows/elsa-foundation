using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Graph.Design.Models;

/// <summary>
/// Provider-owned source for an authored activity graph. The public activity contract is
/// deliberately absent: it remains authoritative in Design Core and is supplied separately.
/// </summary>
public sealed record ActivityGraphManifest(
    string ManifestSchemaVersion,
    JsonElement RootActivity,
    IReadOnlyList<ActivityGraphVariable> Variables,
    IReadOnlyList<ActivityGraphOutputMapping> OutputMappings,
    IReadOnlyList<ActivityGraphOutcomeMapping> OutcomeMappings)
{
    public const string SchemaVersion = "1";
    public const string MultipleOutcomesSchemaVersion = "2";

    public ActivityGraphManifest(
        JsonElement rootActivity,
        IReadOnlyList<ActivityGraphVariable> variables,
        IReadOnlyList<ActivityGraphOutputMapping> outputMappings)
        : this(SchemaVersion, rootActivity, variables, outputMappings, [])
    {
    }

    public void Deconstruct(
        out JsonElement rootActivity,
        out IReadOnlyList<ActivityGraphVariable> variables,
        out IReadOnlyList<ActivityGraphOutputMapping> outputMappings)
    {
        rootActivity = RootActivity;
        variables = Variables;
        outputMappings = OutputMappings;
    }

    public static bool TryParse(
        JsonElement payload,
        out ActivityGraphManifest? manifest,
        out IReadOnlyList<ActivityGraphManifestError> errors) =>
        TryParse(SchemaVersion, payload, out manifest, out errors);

    public static bool TryParse(
        string schemaVersion,
        JsonElement payload,
        out ActivityGraphManifest? manifest,
        out IReadOnlyList<ActivityGraphManifestError> errors)
    {
        var findings = new List<ActivityGraphManifestError>();
        manifest = null;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            findings.Add(new("activity.graph.manifest.object-required", "", "The graph manifest payload must be a JSON object."));
            errors = findings;
            return false;
        }

        var rootActivity = ReadRequiredObject(payload, "rootActivity", "/rootActivity", findings);
        var variables = ReadVariables(payload, findings);
        var outputMappings = ReadOutputMappings(payload, findings);
        var outcomeMappings = StringComparer.Ordinal.Equals(schemaVersion, MultipleOutcomesSchemaVersion)
            ? ReadOutcomeMappings(payload, findings)
            : [];

        if (findings.Count == 0)
            manifest = new(schemaVersion, rootActivity!.Value.Clone(), variables, outputMappings, outcomeMappings);

        errors = findings;
        return manifest is not null;
    }

    /// <summary>
    /// Serializes the selected schema in a deterministic form. Object properties are ordinally sorted at every
    /// level while authored array order is preserved.
    /// </summary>
    public byte[] ToCanonicalUtf8Bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        if (StringComparer.Ordinal.Equals(ManifestSchemaVersion, MultipleOutcomesSchemaVersion))
        {
            writer.WritePropertyName("outcomeMappings");
            writer.WriteStartArray();
            foreach (var mapping in OutcomeMappings)
            {
                writer.WriteStartObject();
                writer.WriteString("boundaryOutcomeReferenceKey", mapping.BoundaryOutcomeReferenceKey);
                writer.WriteString("sourceOutcomeReferenceKey", mapping.SourceOutcomeReferenceKey);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WritePropertyName("outputMappings");
        writer.WriteStartArray();
        foreach (var mapping in OutputMappings)
        {
            writer.WriteStartObject();
            writer.WriteString("outputReferenceKey", mapping.OutputReferenceKey);
            writer.WritePropertyName("source");
            WriteExpression(writer, mapping.Source);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("rootActivity");
        ActivityGraphCanonicalJson.Write(writer, RootActivity);

        writer.WritePropertyName("variables");
        writer.WriteStartArray();
        foreach (var variable in Variables)
        {
            writer.WriteStartObject();
            if (variable.InitialValue is not null)
            {
                writer.WritePropertyName("initialValue");
                WriteExpression(writer, variable.InitialValue);
            }
            writer.WriteString("name", variable.Name);
            writer.WriteString("referenceKey", variable.ReferenceKey);
            writer.WriteString("storageDriverKey", variable.StorageDriverKey);
            writer.WritePropertyName("type");
            writer.WriteStartObject();
            writer.WriteString("alias", variable.Type.Alias);
            writer.WriteString("collectionKind", variable.Type.CollectionKind.ToString());
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    public JsonElement ToCanonicalJsonElement()
    {
        using var document = JsonDocument.Parse(ToCanonicalUtf8Bytes());
        return document.RootElement.Clone();
    }

    private static void WriteExpression(Utf8JsonWriter writer, ActivityInputDefault expression)
    {
        writer.WriteStartObject();
        writer.WriteString("syntax", expression.Syntax);
        writer.WritePropertyName("value");
        ActivityGraphCanonicalJson.Write(writer, expression.Value);
        writer.WriteEndObject();
    }

    private static JsonElement? ReadRequiredObject(
        JsonElement parent,
        string propertyName,
        string pointer,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            findings.Add(new("activity.graph.manifest.object-required", pointer, $"'{propertyName}' must be a JSON object."));
            return null;
        }

        return value;
    }

    private static IReadOnlyList<ActivityGraphVariable> ReadVariables(
        JsonElement payload,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!payload.TryGetProperty("variables", out var variablesElement) || variablesElement.ValueKind != JsonValueKind.Array)
        {
            findings.Add(new("activity.graph.manifest.array-required", "/variables", "'variables' must be a JSON array."));
            return [];
        }

        var variables = new List<ActivityGraphVariable>();
        var index = 0;
        foreach (var element in variablesElement.EnumerateArray())
        {
            var pointer = $"/variables/{index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                findings.Add(new("activity.graph.manifest.object-required", pointer, "Each graph variable must be a JSON object."));
                index++;
                continue;
            }

            var referenceKey = ReadRequiredString(element, "referenceKey", $"{pointer}/referenceKey", findings);
            var name = ReadRequiredString(element, "name", $"{pointer}/name", findings);
            var storageDriverKey = ReadRequiredString(element, "storageDriverKey", $"{pointer}/storageDriverKey", findings);
            var type = ReadType(element, pointer, findings);
            var initialValue = ReadOptionalExpression(element, "initialValue", $"{pointer}/initialValue", findings);

            if (referenceKey is not null && name is not null && storageDriverKey is not null && type is not null)
                variables.Add(new(referenceKey, name, type, storageDriverKey, initialValue));

            index++;
        }

        return variables;
    }

    private static IReadOnlyList<ActivityGraphOutputMapping> ReadOutputMappings(
        JsonElement payload,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!payload.TryGetProperty("outputMappings", out var mappingsElement) || mappingsElement.ValueKind != JsonValueKind.Array)
        {
            findings.Add(new("activity.graph.manifest.array-required", "/outputMappings", "'outputMappings' must be a JSON array."));
            return [];
        }

        var mappings = new List<ActivityGraphOutputMapping>();
        var index = 0;
        foreach (var element in mappingsElement.EnumerateArray())
        {
            var pointer = $"/outputMappings/{index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                findings.Add(new("activity.graph.manifest.object-required", pointer, "Each graph output mapping must be a JSON object."));
                index++;
                continue;
            }

            var referenceKey = ReadRequiredString(element, "outputReferenceKey", $"{pointer}/outputReferenceKey", findings);
            var source = ReadRequiredExpression(element, "source", $"{pointer}/source", findings);
            if (referenceKey is not null && source is not null)
                mappings.Add(new(referenceKey, source));

            index++;
        }

        return mappings;
    }

    private static IReadOnlyList<ActivityGraphOutcomeMapping> ReadOutcomeMappings(
        JsonElement payload,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!payload.TryGetProperty("outcomeMappings", out var mappingsElement) || mappingsElement.ValueKind != JsonValueKind.Array)
        {
            findings.Add(new("activity.graph.manifest.array-required", "/outcomeMappings", "'outcomeMappings' must be a JSON array."));
            return [];
        }

        var mappings = new List<ActivityGraphOutcomeMapping>();
        var index = 0;
        foreach (var element in mappingsElement.EnumerateArray())
        {
            var pointer = $"/outcomeMappings/{index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                findings.Add(new("activity.graph.manifest.object-required", pointer, "Each graph outcome mapping must be a JSON object."));
                index++;
                continue;
            }

            var source = ReadRequiredString(
                element,
                "sourceOutcomeReferenceKey",
                $"{pointer}/sourceOutcomeReferenceKey",
                findings);
            var boundary = ReadRequiredString(
                element,
                "boundaryOutcomeReferenceKey",
                $"{pointer}/boundaryOutcomeReferenceKey",
                findings);
            if (source is not null && boundary is not null)
                mappings.Add(new(source, boundary));

            index++;
        }

        return mappings;
    }

    private static TypeReference? ReadType(
        JsonElement variable,
        string pointer,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!variable.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.Object)
        {
            findings.Add(new("activity.graph.manifest.object-required", $"{pointer}/type", "Variable 'type' must be a JSON object."));
            return null;
        }

        var alias = ReadRequiredString(typeElement, "alias", $"{pointer}/type/alias", findings);
        var collectionKindValue = ReadRequiredString(typeElement, "collectionKind", $"{pointer}/type/collectionKind", findings);
        if (alias is null || collectionKindValue is null)
            return null;

        if (!Enum.TryParse<CollectionKind>(collectionKindValue, false, out var collectionKind) ||
            !StringComparer.Ordinal.Equals(collectionKind.ToString(), collectionKindValue))
        {
            findings.Add(new(
                "activity.graph.manifest.collection-kind-invalid",
                $"{pointer}/type/collectionKind",
                $"'{collectionKindValue}' is not a supported collection kind."));
            return null;
        }

        return new(alias, collectionKind);
    }

    private static ActivityInputDefault? ReadOptionalExpression(
        JsonElement parent,
        string propertyName,
        string pointer,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!parent.TryGetProperty(propertyName, out _))
            return null;

        return ReadRequiredExpression(parent, propertyName, pointer, findings);
    }

    private static ActivityInputDefault? ReadRequiredExpression(
        JsonElement parent,
        string propertyName,
        string pointer,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            findings.Add(new("activity.graph.manifest.expression-required", pointer, $"'{propertyName}' must be an expression object."));
            return null;
        }

        var syntax = ReadRequiredString(element, "syntax", $"{pointer}/syntax", findings);
        if (!element.TryGetProperty("value", out var value))
        {
            findings.Add(new("activity.graph.manifest.value-required", $"{pointer}/value", "Expression 'value' is required."));
            return null;
        }

        return syntax is null ? null : new(syntax, value.Clone());
    }

    private static string? ReadRequiredString(
        JsonElement parent,
        string propertyName,
        string pointer,
        ICollection<ActivityGraphManifestError> findings)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            findings.Add(new("activity.graph.manifest.string-required", pointer, $"'{propertyName}' must be a non-empty string."));
            return null;
        }

        return value.GetString();
    }
}

public sealed record ActivityGraphVariable(
    string ReferenceKey,
    string Name,
    TypeReference Type,
    string StorageDriverKey,
    ActivityInputDefault? InitialValue);

public sealed record ActivityGraphOutputMapping(string OutputReferenceKey, ActivityInputDefault Source);

public sealed record ActivityGraphOutcomeMapping(
    string SourceOutcomeReferenceKey,
    string BoundaryOutcomeReferenceKey);

public sealed record ActivityGraphManifestError(string Code, string JsonPointer, string Message);

internal static class ActivityGraphCanonicalJson
{
    public static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        var pending = new Stack<WriteOperation>();
        pending.Push(new(WriteOperationKind.Value, element));

        while (pending.TryPop(out var operation))
        {
            if (operation.Kind == WriteOperationKind.PropertyName)
            {
                writer.WritePropertyName(operation.PropertyName!);
                continue;
            }

            if (operation.Kind == WriteOperationKind.EndObject)
            {
                writer.WriteEndObject();
                continue;
            }

            if (operation.Kind == WriteOperationKind.EndArray)
            {
                writer.WriteEndArray();
                continue;
            }

            switch (operation.Element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    writer.WriteStartObject();
                    pending.Push(new(WriteOperationKind.EndObject));
                    var properties = operation.Element.EnumerateObject()
                        .OrderBy(x => x.Name, StringComparer.Ordinal)
                        .ToArray();
                    for (var index = properties.Length - 1; index >= 0; index--)
                    {
                        pending.Push(new(WriteOperationKind.Value, properties[index].Value));
                        pending.Push(new(WriteOperationKind.PropertyName, PropertyName: properties[index].Name));
                    }
                    break;
                }
                case JsonValueKind.Array:
                {
                    writer.WriteStartArray();
                    pending.Push(new(WriteOperationKind.EndArray));
                    var items = operation.Element.EnumerateArray().ToArray();
                    for (var index = items.Length - 1; index >= 0; index--)
                        pending.Push(new(WriteOperationKind.Value, items[index]));
                    break;
                }
                case JsonValueKind.String:
                    writer.WriteStringValue(operation.Element.GetString());
                    break;
                case JsonValueKind.Number:
                    WriteNumber(writer, operation.Element);
                    break;
                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    break;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    writer.WriteNullValue();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(element));
            }
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.TryGetInt64(out var signed))
        {
            writer.WriteNumberValue(signed);
            return;
        }

        if (element.TryGetUInt64(out var unsigned))
        {
            writer.WriteNumberValue(unsigned);
            return;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            writer.WriteRawValue(decimalValue.ToString("G29", CultureInfo.InvariantCulture), skipInputValidation: true);
            return;
        }

        writer.WriteNumberValue(element.GetDouble());
    }

    private enum WriteOperationKind
    {
        Value,
        PropertyName,
        EndObject,
        EndArray
    }

    private readonly record struct WriteOperation(
        WriteOperationKind Kind,
        JsonElement Element = default,
        string? PropertyName = null);
}
