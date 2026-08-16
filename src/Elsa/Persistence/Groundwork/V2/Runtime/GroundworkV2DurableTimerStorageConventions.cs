using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current durable-timer envelope, identity, and projected fields.</summary>
internal static class GroundworkV2DurableTimerStorageConventions
{
    public static StorageValues Values(DurableTimer timer) => Values(NewEnvelope(timer));

    public static StorageValues Values(GroundworkV2DurableTimerEnvelope envelope)
    {
        ValidateEnvelope(envelope);
        return GroundworkRuntimeRowStore.Values(
            PhysicalId(envelope.Timer.WorkflowExecutionId, envelope.Timer.TimerId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(envelope),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = envelope.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.DurableTimerIdField] = envelope.Timer.TimerId,
                [ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField] = envelope.Timer.DueTime,
                [ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField] = envelope.ClaimOrderKey
            });
    }

    public static GroundworkV2DurableTimerEnvelope Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork durable-timer row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork durable-timer row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork durable-timer row did not contain JSON content.");

        GroundworkV2DurableTimerEnvelope envelope;
        try
        {
            envelope = GroundworkV2RuntimeJson.Deserialize<GroundworkV2DurableTimerEnvelope>(content)
                       ?? throw new InvalidDataException("Groundwork durable-timer row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork durable-timer row content was not valid current JSON.", exception);
        }

        ValidateEnvelope(envelope);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.IdField,
            PhysicalId(envelope.Timer.WorkflowExecutionId, envelope.Timer.TimerId));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.CollectionField, ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, envelope.Timer.WorkflowExecutionId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.DurableTimerIdField, envelope.Timer.TimerId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField, envelope.Timer.DueTime);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField, envelope.ClaimOrderKey);
        return envelope;
    }

    public static string PhysicalId(string workflowExecutionId, string timerId) =>
        GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, timerId);

    public static string ClaimOrderKey(DateTimeOffset availableAt, DurableTimer timer)
    {
        ValidateIdentity(timer.WorkflowExecutionId, timer.TimerId);
        var key = string.Concat(
            availableAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture),
            ".",
            StableHash(GroundworkV2CompositeIdentityCodec.From(timer.WorkflowExecutionId, timer.TimerId)));
        if (key.Length > ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyProjectionLength)
        {
            throw new InvalidOperationException(
                $"Groundwork durable-timer claim order key exceeds the admitted length of {ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyProjectionLength}.");
        }

        return key;
    }

    public static string ClaimOrderUpperBound(DateTimeOffset asOf) =>
        $"{asOf.UtcTicks:D19}.~";

    public static GroundworkV2DurableTimerEnvelope NewEnvelope(DurableTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        return new GroundworkV2DurableTimerEnvelope(
            ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
            timer.WorkflowExecutionId,
            ClaimOrderKey(timer.DueTime, timer),
            null,
            0,
            null,
            null,
            0,
            timer);
    }

    private static void ValidateEnvelope(GroundworkV2DurableTimerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!StringComparer.Ordinal.Equals(envelope.Collection, ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind))
            throw new InvalidDataException("Groundwork durable-timer row collection does not match its current unit.");

        ValidateIdentity(envelope.WorkflowExecutionId, envelope.Timer.TimerId);
        if (!StringComparer.Ordinal.Equals(envelope.WorkflowExecutionId, envelope.Timer.WorkflowExecutionId))
            throw new InvalidDataException("Groundwork durable-timer envelope workflow identity does not match its timer.");
        if (envelope.ClaimToken < 0 || envelope.FailureCount < 0)
            throw new InvalidDataException("Groundwork durable-timer claim counters cannot be negative.");
        if (envelope.ClaimToken == 0 && envelope.FailureCount != 0)
            throw new InvalidDataException("Groundwork durable-timer initial failure count must be zero.");
        if (envelope.ClaimToken == 0 &&
            (envelope.ClaimOwnerId is not null || envelope.ClaimedAt is not null || envelope.VisibleAfter is not null))
        {
            throw new InvalidDataException("Groundwork durable-timer initial claim state is inconsistent.");
        }
        if (envelope.ClaimToken > 0 && envelope.ClaimOwnerId is null &&
            (envelope.ClaimedAt is not null || envelope.VisibleAfter is null))
        {
            throw new InvalidDataException("Groundwork durable-timer released claim state is inconsistent.");
        }
        if (envelope.ClaimToken > 0 && envelope.ClaimOwnerId is not null &&
            (envelope.ClaimedAt is null || envelope.VisibleAfter is null))
        {
            throw new InvalidDataException("Groundwork durable-timer claim state is incomplete.");
        }
        if (envelope.ClaimedAt is { } claimedAt && envelope.VisibleAfter is { } visibleAfter && visibleAfter <= claimedAt)
            throw new InvalidDataException("Groundwork durable-timer visibility deadline must follow its claim time.");

        var expectedClaimOrder = ClaimOrderKey(envelope.VisibleAfter ?? envelope.Timer.DueTime, envelope.Timer);
        if (!StringComparer.Ordinal.Equals(envelope.ClaimOrderKey, expectedClaimOrder))
            throw new InvalidDataException("Groundwork durable-timer claim order projection does not match its current state.");
    }

    private static void ValidateIdentity(string workflowExecutionId, string timerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork durable-timer row projection '{field}' does not match its current content.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset expected)
    {
        var actual = RequiredDateTime(values, field);
        if (actual != expected)
            throw new InvalidDataException($"Groundwork durable-timer row projection '{field}' does not match its current content.");
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

        throw new InvalidDataException($"Groundwork durable-timer row is missing required string field '{field}'.");
    }

    private static DateTimeOffset RequiredDateTime(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset;
            if (value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;
            if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
                DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                return parsed;
            }
        }

        throw new InvalidDataException($"Groundwork durable-timer row is missing required timestamp field '{field}'.");
    }

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed record GroundworkV2DurableTimerEnvelope(
    string Collection,
    string WorkflowExecutionId,
    string ClaimOrderKey,
    string? ClaimOwnerId,
    long ClaimToken,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? VisibleAfter,
    int FailureCount,
    DurableTimer Timer);
