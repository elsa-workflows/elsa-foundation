using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current activity-execution inspection envelope and summary projections.</summary>
internal static class GroundworkV2ActivityExecutionInspectionStorageConventions
{
    public static string PhysicalId(string workflowExecutionId, string activityExecutionId) =>
        GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, activityExecutionId);

    public static StorageValues Values(ActivityExecutionInspectionProjection projection)
    {
        Validate(projection);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(projection.WorkflowExecutionId, projection.ActivityExecutionId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(projection),
            Projections(projection));
    }

    public static IReadOnlyDictionary<string, object?> Projections(
        ActivityExecutionInspectionProjection projection)
    {
        Validate(projection);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = projection.WorkflowExecutionId,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField] = projection.ExecutionSequence,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryScheduledAtField] = projection.ScheduledAt,
            [ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField] = projection.ActivityExecutionId
        };
    }

    public static ActivityExecutionInspectionProjection Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution inspection row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException(
                    "Groundwork activity-execution inspection row content is not JSON.")
            }
            : throw new InvalidDataException(
                "Groundwork activity-execution inspection row did not contain JSON content.");

        ActivityExecutionInspectionProjection projection;
        try
        {
            projection = GroundworkV2RuntimeJson.Deserialize<ActivityExecutionInspectionProjection>(content)
                         ?? throw new InvalidDataException(
                             "Groundwork activity-execution inspection row content was empty.");
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
                "Groundwork activity-execution inspection row content was not valid current JSON.",
                exception);
        }

        try
        {
            Validate(projection);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException(
                "Groundwork activity-execution inspection row content failed current validation.",
                exception);
        }
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(projection.WorkflowExecutionId, projection.ActivityExecutionId));
        foreach (var (field, expected) in Projections(projection))
            EnsureProjection(values, field, expected);
        return projection;
    }

    public static void Validate(ActivityExecutionInspectionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.AuthoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityTypeVersion);
        ArgumentNullException.ThrowIfNull(projection.Provenance);
        ArgumentNullException.ThrowIfNull(projection.OutcomeNames);
        ArgumentNullException.ThrowIfNull(projection.Bookmarks);
        ArgumentNullException.ThrowIfNull(projection.Incidents);
        ArgumentNullException.ThrowIfNull(projection.ValueSnapshots);
        ArgumentNullException.ThrowIfNull(projection.Metadata);
        if (projection.ExecutionSequence < 0)
            throw new ArgumentOutOfRangeException(
                nameof(projection),
                "Activity-execution inspection sequence cannot be negative.");
        _ = PhysicalId(projection.WorkflowExecutionId, projection.ActivityExecutionId);
        if (projection.Provenance.SchedulingWorkflowExecutionId is { } schedulingWorkflowId &&
            !StringComparer.Ordinal.Equals(schedulingWorkflowId, projection.WorkflowExecutionId))
        {
            throw new InvalidOperationException(
                "Activity-execution inspection provenance workflow identity does not match its projection.");
        }
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !EqualsProjected(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution inspection row projection '{field}' does not match its current content.");
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
                JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
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

        return Equals(actual, expected);
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, object?> values,
        string field)
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
            $"Groundwork activity-execution inspection row is missing required string field '{field}'.");
    }
}
