using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current recurring-trigger schedule envelope, identity, and query projections.</summary>
internal static class GroundworkV2RecurringTriggerScheduleStorageConventions
{
    public static string PhysicalId(string scheduleId)
    {
        ValidateScheduleId(scheduleId);
        return scheduleId;
    }

    public static StorageValues Values(RecurringTriggerSchedule schedule)
    {
        Validate(schedule);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(schedule.ScheduleId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(schedule),
            Projections(schedule));
    }

    public static IReadOnlyDictionary<string, object?> Projections(RecurringTriggerSchedule schedule)
    {
        Validate(schedule);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.CollectionField] =
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind,
            [ElsaRuntimeV2StorageManifest.ArtifactIdField] = schedule.ArtifactId,
            [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleActivationIdField] = schedule.ActivationId,
            [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField] = schedule.ScheduleId,
            [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField] = schedule.IsActive,
            [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField] = schedule.NextOccurrence
        };
    }

    public static RecurringTriggerSchedule Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-trigger schedule row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork recurring-trigger schedule row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork recurring-trigger schedule row did not contain JSON content.");

        RecurringTriggerSchedule schedule;
        try
        {
            schedule = GroundworkV2RuntimeJson.Deserialize<RecurringTriggerSchedule>(content)
                       ?? throw new InvalidDataException("Groundwork recurring-trigger schedule row content was empty.");
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
                "Groundwork recurring-trigger schedule row content was not valid current JSON.",
                exception);
        }

        Validate(schedule);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(schedule.ScheduleId));
        foreach (var (field, expected) in Projections(schedule))
            EnsureProjection(values, field, expected);
        return schedule;
    }

    public static void Validate(RecurringTriggerSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ValidateScheduleId(schedule.ScheduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.StimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.StimulusHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.Expression);
        if (!Enum.IsDefined(schedule.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedule.Kind),
                schedule.Kind,
                "The recurring-trigger schedule kind is not defined.");
        }

        ValidateProjectionLength(
            schedule.ArtifactId,
            ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength,
            nameof(schedule.ArtifactId));
        if (schedule.ActivationId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ActivationId);
            ValidateProjectionLength(
                schedule.ActivationId,
                ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength,
                nameof(schedule.ActivationId));
        }

        if (schedule.ActivationId is null && schedule.SlotId is not null)
            throw new ArgumentException("A recurring-trigger schedule slot requires an activation.", nameof(schedule));
        if (schedule.SlotId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SlotId);

        var expectedId = schedule.ActivationId is null
            ? RecurringTriggerSchedule.BuildId(schedule.ArtifactId, schedule.ExecutableNodeId)
            : RecurringTriggerSchedule.BuildId(
                schedule.ActivationId,
                schedule.ArtifactId,
                schedule.ExecutableNodeId);
        var expectedFanOutId = schedule.ActivationId is null
            ? RecurringTriggerSchedule.BuildFanOutId(
                schedule.ArtifactId,
                schedule.ExecutableNodeId,
                schedule.StimulusHash)
            : RecurringTriggerSchedule.BuildFanOutId(
                schedule.ActivationId,
                schedule.ArtifactId,
                schedule.ExecutableNodeId,
                schedule.StimulusHash);
        if (!StringComparer.Ordinal.Equals(schedule.ScheduleId, expectedId) &&
            !StringComparer.Ordinal.Equals(schedule.ScheduleId, expectedFanOutId))
        {
            throw new ArgumentException(
                "The recurring-trigger schedule id does not match its deterministic identity.",
                nameof(schedule));
        }
    }

    public static void ValidateScheduleId(string scheduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        ValidateProjectionLength(
            scheduleId,
            ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength,
            nameof(scheduleId));
        _ = GroundworkRuntimeRowStore.Key(scheduleId);
    }

    private static void ValidateProjectionLength(string value, int maximum, string parameterName)
    {
        if (value.Length > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Groundwork recurring-trigger schedule projection '{parameterName}' cannot exceed {maximum} characters.");
        }
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object? expected)
    {
        if (!values.TryGetValue(field, out var actual) || !Equivalent(actual, expected))
        {
            throw new InvalidDataException(
                $"Groundwork recurring-trigger schedule row projection '{field}' does not match its current content.");
        }
    }

    private static bool Equivalent(object? actual, object? expected)
    {
        if (actual is JsonElement element)
        {
            actual = element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String when expected is DateTimeOffset => element.GetDateTimeOffset(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True or JsonValueKind.False when expected is bool => element.GetBoolean(),
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
        {
            return parsed == expectedDate;
        }

        if (expected is null)
        {
            return actual is null || actual is JsonElement { ValueKind: JsonValueKind.Null };
        }

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
            $"Groundwork recurring-trigger schedule row is missing required string field '{field}'.");
    }
}

internal sealed record GroundworkV2RecurringTriggerScheduleProjectionState(
    string ProjectionKind,
    string ActivationId,
    string? ArtifactId,
    bool IsActive,
    int ScheduleCount,
    string ProjectionFingerprint,
    IReadOnlyList<string> ScheduleIds,
    IReadOnlyDictionary<string, string> ScheduleFingerprints);
