using System.Text;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Strict runtime interpreter for publication-pinned value-conversion plans.
/// It deliberately contains no converter discovery: formatted-content, XML, profiles, and external
/// reference materialization are introduced by their dedicated runtime slices.
/// </summary>
public sealed class RuntimeValueConversionExecutor : IRuntimeValueConversionExecutor
{
    public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        ValidatePlan(plan);
        ValidateRepresentation(plan);
        if (!ValueConversionCompatibility.SameType(source.Type, plan.SourceType))
            throw Reject(plan, $"runtime source contract '{source.Type.Alias} ({source.Type.CollectionKind})' does not match the pinned source contract");

        if (source.ExternalReference is not null && plan.Operation is not ValueConversionOperation.Identity and not ValueConversionOperation.NullableCompatibility)
            throw Reject(plan, "external-reference conversion is not available in this runtime slice");

        if (source.Presence != ValuePresence.Present)
            return new ValueEnvelope(plan.TargetType, source.Presence, null, null, source.Policy);

        if (source.ExternalReference is not null)
            return ValueEnvelope.External(plan.TargetType, source.ExternalReference, source.Policy);

        var inline = source.InlineValue
            ?? throw Reject(plan, "a present source value did not contain an inline payload");
        ValidatePayloadLimits(plan, inline);

        var converted = plan.Operation switch
        {
            ValueConversionOperation.Identity => inline.Clone(),
            ValueConversionOperation.NullableCompatibility => inline.Clone(),
            ValueConversionOperation.NumericWidening => ConvertNumeric(inline, plan),
            ValueConversionOperation.RecursiveCollection => ConvertCollection(inline, plan, depth: 1),
            ValueConversionOperation.CanonicalAny => CanonicalizeAny(inline, plan),
            ValueConversionOperation.Profile => throw Reject(plan, "pinned conversion profiles are not available in this runtime slice"),
            _ => throw Reject(plan, $"the pinned operation '{plan.Operation}' is unsupported")
        };

        return ValueEnvelope.Inline(plan.TargetType, converted, source.Policy);
    }

    private static void ValidatePlan(ValueConversionPlan plan)
    {
        if (plan.Mode is ValueConversionMode.Json or ValueConversionMode.Xml or ValueConversionMode.Profile ||
            plan.Operation == ValueConversionOperation.Profile)
            throw Reject(plan, "pinned conversion profiles are not available in this runtime slice");

        switch (plan.Operation)
        {
            case ValueConversionOperation.Identity when !ValueConversionCompatibility.SameType(plan.SourceType, plan.TargetType):
                throw Reject(plan, "identity conversion requires matching source and target contracts");
            case ValueConversionOperation.NullableCompatibility when !ValueConversionCompatibility.IsNullableCompatibility(plan.SourceType, plan.TargetType):
                throw Reject(plan, "nullable compatibility requires matching source and target contracts");
            case ValueConversionOperation.NumericWidening when !ValueConversionCompatibility.IsSafeNumericWidening(plan.SourceType, plan.TargetType):
                throw Reject(plan, "numeric conversion is not a safe widening");
            case ValueConversionOperation.RecursiveCollection when !ValueConversionCompatibility.CanRecursivelyWidenCollection(plan.SourceType, plan.TargetType):
                throw Reject(plan, "recursive collection conversion does not have a safe element widening");
            case ValueConversionOperation.CanonicalAny when !ValueConversionCompatibility.IsCanonicalAnyTarget(plan.TargetType):
                throw Reject(plan, "canonical dynamic projection requires the Elsa.Any target contract");
        }
    }

    private static void ValidateRepresentation(ValueConversionPlan plan)
    {
        if (plan.SourceRepresentation == ValueRepresentation.TransientResource)
            throw Reject(plan, "transient resources cannot cross a durable conversion boundary");

        if (plan.SourceRepresentation is ValueRepresentation.FormattedContent or ValueRepresentation.DurableReference &&
            plan.Operation is not ValueConversionOperation.Identity and not ValueConversionOperation.NullableCompatibility)
            throw Reject(plan, "the source representation requires a dedicated pinned profile or external-reference conversion");
    }

    private static JsonElement ConvertNumeric(JsonElement value, ValueConversionPlan plan)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Reject(plan, "numeric widening requires a JSON number source value");

        return value.Clone();
    }

    private static JsonElement ConvertCollection(JsonElement value, ValueConversionPlan plan, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");
        if (value.ValueKind != JsonValueKind.Array)
            throw Reject(plan, "recursive collection conversion requires a JSON array source value");

        var values = value.EnumerateArray().ToArray();
        if (values.Length > plan.Limits.MaxCollectionItems)
            throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");

        // The plan has only one element contract. It therefore supports identity and numeric element
        // widening here; nested collection shape is represented by the JSON payload but is not inferred.
        if (ValueConversionCompatibility.CanSafelyWidenElement(plan.SourceType, plan.TargetType) && values.Any(item => item.ValueKind != JsonValueKind.Number))
            throw Reject(plan, "recursive numeric widening requires every collection element to be a JSON number");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in values)
                item.WriteTo(writer);
            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement CanonicalizeAny(JsonElement value, ValueConversionPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(writer, value, plan, depth: 1);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value, ValueConversionPlan plan, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Length > plan.Limits.MaxCollectionItems)
                    throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");
                writer.WriteStartObject();
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value, plan, depth + 1);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                var items = value.EnumerateArray().ToArray();
                if (items.Length > plan.Limits.MaxCollectionItems)
                    throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");
                writer.WriteStartArray();
                foreach (var item in items)
                    WriteCanonicalJson(writer, item, plan, depth + 1);
                writer.WriteEndArray();
                return;
            default:
                value.WriteTo(writer);
                return;
        }
    }

    private static void ValidatePayloadLimits(ValueConversionPlan plan, JsonElement value)
    {
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > plan.Limits.MaxPayloadBytes)
            throw Reject(plan, $"maximum payload size '{plan.Limits.MaxPayloadBytes}' bytes was exceeded");
    }

    private static RuntimeValueConversionException Reject(ValueConversionPlan plan, string reason) => new(plan, reason);
}
