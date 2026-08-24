using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current executable-activity-template and hash-claim row envelopes.</summary>
internal static class GroundworkV2ExecutableActivityTemplateStorageConventions
{
    private const string Collection = ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind;
    private const string NodesByIdProperty = "nodesById";

    public static StorageValues Values(ExecutableActivityTemplate template)
    {
        Validate(template);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(template.TemplateId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            SerializeEnvelope(template),
            Projections(template));
    }

    public static StorageValues ClaimValues(ExecutableActivityTemplate template)
    {
        Validate(template);
        return GroundworkRuntimeRowStore.Values(
            HashClaimId(template.TemplateHash),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(new TemplateHashClaim(template.TemplateHash, template.TemplateId)));
    }

    public static ExecutableActivityTemplate Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        EnsureSchemaVersion(schemaVersion, "executable activity template");
        var content = Content(values, "executable activity template");

        try
        {
            var envelope = JsonNode.Parse(content)?.AsObject()
                           ?? throw new InvalidDataException("Groundwork executable activity template row content was empty.");
            var collection = RequiredNodeString(envelope, "collection");
            if (!StringComparer.Ordinal.Equals(collection, Collection))
                throw new InvalidDataException("Groundwork executable activity template row has an unexpected collection.");

            var envelopeHash = RequiredNodeString(envelope, "templateHash");
            var templateNode = envelope["template"]
                               ?? throw new InvalidDataException("Groundwork executable activity template row omitted its template payload.");
            var template = GroundworkV2RuntimeJson.Deserialize<ExecutableActivityTemplate>(templateNode.ToJsonString())
                           ?? throw new InvalidDataException("Groundwork executable activity template payload was empty.");
            Validate(template);
            if (!StringComparer.Ordinal.Equals(envelopeHash, template.TemplateHash))
                throw new InvalidDataException("Groundwork executable activity template envelope hash does not match its payload.");

            EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, PhysicalId(template.TemplateId));
            foreach (var (field, expected) in Projections(template))
                EnsureProjection(values, field, expected);
            return template;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or FormatException
                                          or OverflowException)
        {
            throw new InvalidDataException(
                "Groundwork executable activity template row content was not valid current JSON.",
                exception);
        }
    }

    public static TemplateHashClaim DeserializeClaim(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        EnsureSchemaVersion(schemaVersion, "executable activity template hash claim");
        var content = Content(values, "executable activity template hash claim");
        try
        {
            var claim = GroundworkV2RuntimeJson.Deserialize<TemplateHashClaim>(content)
                        ?? throw new InvalidDataException("Groundwork executable activity template hash claim content was empty.");
            ValidateClaim(claim);
            EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, HashClaimId(claim.TemplateHash));
            return claim;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or FormatException
                                          or OverflowException)
        {
            throw new InvalidDataException(
                "Groundwork executable activity template hash claim content was not valid current JSON.",
                exception);
        }
    }

    public static string PhysicalId(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        if (templateId.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(templateId), "Executable activity template ids cannot exceed 128 characters.");
        _ = GroundworkRuntimeRowStore.Key(templateId);
        return templateId;
    }

    public static string HashClaimId(string templateHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);
        var id = $"templateHash:{templateHash.Length}:{templateHash}";
        if (id.Length > ElsaRuntimeV2StorageManifest.IdMaximumLength)
            throw new ArgumentOutOfRangeException(nameof(templateHash), "Executable activity template hash claims exceed the bounded row identity.");
        _ = GroundworkRuntimeRowStore.Key(id);
        return id;
    }

    public static IReadOnlyDictionary<string, object?> Projections(ExecutableActivityTemplate template)
    {
        Validate(template);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] = Collection,
            [ElsaRuntimeV2StorageManifest.TemplateHashField] = template.TemplateHash,
            [ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateIdField] = template.TemplateId
        };
    }

    public static void Validate(ExecutableActivityTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _ = PhysicalId(template.TemplateId);
        _ = HashClaimId(template.TemplateHash);
        if (template.TemplateHash.Length > ElsaRuntimeV2StorageManifest.IdMaximumLength)
            throw new ArgumentOutOfRangeException(nameof(template.TemplateHash));
    }

    private static void ValidateClaim(TemplateHashClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        _ = PhysicalId(claim.TemplateId);
        _ = HashClaimId(claim.TemplateHash);
    }

    private static string SerializeEnvelope(ExecutableActivityTemplate template)
    {
        var envelope = JsonNode.Parse(GroundworkV2RuntimeJson.Serialize(
                new TemplateEnvelope(Collection, template.TemplateHash, template)))?.AsObject()
                       ?? throw new InvalidDataException("Groundwork executable activity template envelope could not be created.");
        if (envelope["template"] is not JsonObject payload)
            throw new InvalidDataException("Groundwork executable activity template envelope omitted its payload.");
        payload.Remove(NodesByIdProperty);
        return envelope.ToJsonString();
    }

    private static string Content(IReadOnlyDictionary<string, object?> values, string name)
    {
        if (!values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent))
            throw new InvalidDataException($"Groundwork {name} row did not contain JSON content.");
        return rawContent switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new InvalidDataException($"Groundwork {name} row content is not JSON.")
        };
    }

    private static void EnsureSchemaVersion(string schemaVersion, string name)
    {
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException(
                $"Groundwork {name} row returned unsupported schema version '{schemaVersion}'.");
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;
            if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
                !string.IsNullOrWhiteSpace(element.GetString()))
                return element.GetString()!;
        }

        throw new InvalidDataException($"Groundwork executable activity template row is missing required string field '{field}'.");
    }

    private static string RequiredNodeString(JsonObject node, string property)
    {
        if (node[property] is JsonValue value && value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
            return text;
        throw new InvalidDataException($"Groundwork executable activity template envelope is missing required field '{property}'.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !Equivalent(actual, expected))
            throw new InvalidDataException(
                $"Groundwork executable activity template row projection '{field}' does not match its current content.");
    }

    private static bool Equivalent(object? actual, object? expected)
    {
        if (actual is JsonElement element)
        {
            actual = element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => element.GetString(),
                _ => actual
            };
        }

        if (actual is DateTime dateTime && expected is DateTimeOffset expectedOffset)
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)) == expectedOffset;
        if (actual is string text && expected is DateTimeOffset expectedDateTime &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed == expectedDateTime;
        return Equals(actual, expected);
    }

    internal sealed record TemplateHashClaim(string TemplateHash, string TemplateId);

    private sealed record TemplateEnvelope(
        string Collection,
        string TemplateHash,
        ExecutableActivityTemplate Template);
}
