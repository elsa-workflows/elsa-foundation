using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current workflow test-scope row envelope and indexed lifecycle projections.</summary>
internal static class GroundworkV2WorkflowTestScopeStorageConventions
{
    public static StorageValues Values(WorkflowTestScopeRecord record)
    {
        Validate(record);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(record.Scope.ScopeId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(record),
            Projections(record));
    }

    public static WorkflowTestScopeRecord Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException($"Groundwork workflow test-scope row returned unsupported schema version '{schemaVersion}'.");

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow test-scope row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow test-scope row did not contain JSON content.");

        WorkflowTestScopeRecord record;
        try
        {
            record = GroundworkV2RuntimeJson.Deserialize<WorkflowTestScopeRecord>(content)
                     ?? throw new InvalidDataException("Groundwork workflow test-scope row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork workflow test-scope row content was not valid current JSON.", exception);
        }

        Validate(record);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, PhysicalId(record.Scope.ScopeId));
        foreach (var (field, expected) in Projections(record))
            EnsureProjection(values, field, expected);
        return record;
    }

    public static string PhysicalId(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        if (scopeId.Length > WorkflowTestScope.MaximumScopeIdLength)
            throw new ArgumentOutOfRangeException(nameof(scopeId));
        return scopeId;
    }

    public static IReadOnlyDictionary<string, object?> Projections(WorkflowTestScopeRecord record)
    {
        Validate(record);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind,
            [ElsaRuntimeV2StorageManifest.StateField] = record.State.ToString(),
            [ElsaRuntimeV2StorageManifest.ScopeIdField] = record.Scope.ScopeId,
            [ElsaRuntimeV2StorageManifest.ExpiresAtField] = record.Scope.ExpiresAt
        };
    }

    public static void Validate(WorkflowTestScopeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Scope);
        _ = PhysicalId(record.Scope.ScopeId);
        if (!Enum.IsDefined(record.State))
            throw new InvalidDataException("Groundwork workflow test-scope row has an unknown lifecycle state.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
            throw new InvalidDataException($"Groundwork workflow test-scope row projection '{field}' does not match its current content.");
    }

    private static bool EqualsProjected(object? actual, object? expected)
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

        throw new InvalidDataException($"Groundwork workflow test-scope row is missing required string field '{field}'.");
    }
}
