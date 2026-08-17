using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current scheduler-poison row identity, envelope, and ordered projections.</summary>
internal static class GroundworkV2WorkflowSchedulerPoisonStorageConventions
{
    public static string PhysicalId(string workflowExecutionId, string workItemId) =>
        GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, workItemId);

    public static StorageValues Values(RuntimeSchedulerPoisonRecord record)
    {
        Validate(record);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(record.WorkflowExecutionId, record.WorkItemId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(record),
            Projections(record));
    }

    public static IReadOnlyDictionary<string, object?> Projections(RuntimeSchedulerPoisonRecord record)
    {
        Validate(record);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = record.WorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.SchedulerPoisonWorkItemIdField] = record.WorkItemId,
            [ElsaRuntimeV2StorageManifest.SchedulerPoisonFirstFailedAtField] = record.FirstFailedAt,
            [ElsaRuntimeV2StorageManifest.SchedulerPoisonLastFailedAtField] = record.LastFailedAt
        };
    }

    public static RuntimeSchedulerPoisonRecord Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork scheduler-poison row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork scheduler-poison row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork scheduler-poison row did not contain JSON content.");

        RuntimeSchedulerPoisonRecord record;
        try
        {
            record = GroundworkV2RuntimeJson.Deserialize<RuntimeSchedulerPoisonRecord>(content)
                     ?? throw new InvalidDataException("Groundwork scheduler-poison row content was empty.");
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
                "Groundwork scheduler-poison row content was not valid current JSON.", exception);
        }

        Validate(record);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(record.WorkflowExecutionId, record.WorkItemId));
        foreach (var (field, expected) in Projections(record))
            EnsureProjection(values, field, expected);
        return record;
    }

    public static void Validate(RuntimeSchedulerPoisonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = PhysicalId(record.WorkflowExecutionId, record.WorkItemId);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork scheduler-poison row projection '{field}' does not match its current content.");
        }
    }

    private static bool EqualsProjected(object? actual, object? expected)
    {
        if (actual is JsonElement element)
        {
            actual = element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String when expected is DateTimeOffset => element.GetDateTimeOffset(),
                JsonValueKind.String => element.GetString(),
                _ => actual
            };
        }

        if (actual is DateTime dateTime && expected is DateTimeOffset expectedOffset)
        {
            var actualOffset = dateTime.Kind switch
            {
                DateTimeKind.Utc => new DateTimeOffset(dateTime),
                DateTimeKind.Local => new DateTimeOffset(dateTime),
                _ => new DateTimeOffset(dateTime, expectedOffset.Offset)
            };
            return actualOffset == expectedOffset;
        }

        if (actual is string actualText && expected is DateTimeOffset expectedDate &&
            DateTimeOffset.TryParse(actualText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed == expectedDate;

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
            {
                return element.GetString()!;
            }
        }

        throw new InvalidDataException(
            $"Groundwork scheduler-poison row is missing required string field '{field}'.");
    }
}
