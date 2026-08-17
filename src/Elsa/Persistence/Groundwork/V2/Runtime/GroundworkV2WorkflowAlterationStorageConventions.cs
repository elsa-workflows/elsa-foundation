using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using System.Security.Cryptography;
using System.Text;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current v2 row envelope, projections, and identity rules for alteration plans and jobs.</summary>
/// <remarks>
/// Plan content also carries the private cleanup marker used while an unsealed capture is being drained. Job content
/// remains the complete current public job model because the runtime checkpoint writer shares that row. Projections
/// are only query accelerators; deserialization verifies them against the content so a corrupted row cannot become
/// authority for a later transition.
/// </remarks>
internal static class GroundworkV2WorkflowAlterationStorageConventions
{
    public static string PhysicalPlanId(string planId) => PhysicalId(planId, "plan");

    public static string PhysicalJobId(string jobId) => PhysicalId(jobId, "job");

    public static StorageValues Values(WorkflowAlterationPlanDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var plan = document.Plan;
        return GroundworkRuntimeRowStore.Values(
            PhysicalPlanId(plan.PlanId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(document),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdField] = plan.PlanId,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantPartitionField] = plan.AuthorityScope.TenantPartition,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField] = plan.IdempotencyKeyHash,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField] = TenantIdempotencyKey(plan.AuthorityScope.TenantPartition, plan.IdempotencyKeyHash),
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField] = plan.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanActiveOrderKeyField] = document.ActiveOrderKey
            });
    }

    public static StorageValues Values(WorkflowAlterationJobDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var job = document.Job;
        return GroundworkRuntimeRowStore.Values(
            PhysicalJobId(job.JobId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            // Jobs are also mutated by the v2 runtime checkpoint writer. Keep the row content as the
            // public current job model so both write paths share one durable representation; the
            // claimable-at value remains a query projection derived from that model.
            GroundworkV2RuntimeJson.Serialize(job),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField] = job.JobId,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobPlanIdField] = job.PlanId,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCaptureOrdinalField] = job.CaptureOrdinal,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField] = document.ClaimableAt,
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField] = job.Status.ToString(),
                [ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField] = job.CheckpointCommitId
            });
    }

    public static WorkflowAlterationPlanDocument DeserializePlan(IReadOnlyDictionary<string, object?> values)
    {
        EnsureSchema(values, "workflow alteration plan");
        var document = DeserializeContent<WorkflowAlterationPlanDocument>(values, "workflow alteration plan");
        ArgumentNullException.ThrowIfNull(document.Plan);
        if (!StringComparer.Ordinal.Equals(document.PlanId, document.Plan.PlanId))
            throw new InvalidDataException("Groundwork workflow alteration plan content identity does not match its current plan.");
        EnsureString(values, ElsaRuntimeV2StorageManifest.IdField, PhysicalPlanId(document.Plan.PlanId));
        EnsureString(values, ElsaRuntimeV2StorageManifest.CollectionField, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdField, document.Plan.PlanId);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantPartitionField, document.Plan.AuthorityScope.TenantPartition);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanIdempotencyKeyHashField, document.Plan.IdempotencyKeyHash);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanTenantIdempotencyKeyField, TenantIdempotencyKey(document.Plan.AuthorityScope.TenantPartition, document.Plan.IdempotencyKeyHash));
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanStatusField, document.Plan.Status.ToString());
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanActiveOrderKeyField, document.ActiveOrderKey);
        return document;
    }

    public static WorkflowAlterationJobDocument DeserializeJob(IReadOnlyDictionary<string, object?> values)
    {
        EnsureSchema(values, "workflow alteration job");
        var job = DeserializeContent<WorkflowAlterationJobState>(values, "workflow alteration job");
        EnsureString(values, ElsaRuntimeV2StorageManifest.IdField, PhysicalJobId(job.JobId));
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobIdField, job.JobId);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobPlanIdField, job.PlanId);
        EnsureInt64(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCaptureOrdinalField, job.CaptureOrdinal);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobStatusField, job.Status.ToString());
        EnsureOptionalString(values, ElsaRuntimeV2StorageManifest.WorkflowAlterationJobCheckpointCommitIdField, job.CheckpointCommitId);
        var document = new WorkflowAlterationJobDocument(
            ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
            job.JobId,
            job.PlanId,
            job.CaptureOrdinal,
            ClaimableAt(job),
            job);
        if (!TryReadDateTime(values.GetValueOrDefault(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField), out var projectionClaimableAt) ||
            projectionClaimableAt != document.ClaimableAt)
        {
            if (document.ClaimableAt is not null || values.ContainsKey(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobClaimableAtField))
                throw new InvalidDataException("Groundwork workflow alteration job claimable-at projection does not match its current content.");
        }

        return document;
    }

    public static string TenantIdempotencyKey(string tenantPartition, string idempotencyKeyHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantPartition);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKeyHash);
        return $"v2:{StableHash(Framed("tenant-idempotency", tenantPartition, idempotencyKeyHash))}";
    }

    public static WorkflowAlterationPlanDocument CreatePlanDocument(WorkflowAlterationPlanState plan, string? activeOrderKey = null, WorkflowAlterationUnsealedCaptureCleanup? cleanup = null) =>
        new(
            ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind,
            plan.PlanId,
            activeOrderKey ?? $"{plan.CreatedAt.UtcTicks:D19}:{plan.PlanId}",
            plan,
            cleanup);

    public static WorkflowAlterationJobDocument CreateJobDocument(WorkflowAlterationJobState job) =>
        new(
            ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
            job.JobId,
            job.PlanId,
            job.CaptureOrdinal,
            ClaimableAt(job),
            job);

    public static string CreateJobId(string planId, string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return $"alteration-job-{StableHash(Framed("job", planId, workflowExecutionId))[..32]}";
    }

    private static string Framed(params string[] components)
    {
        var builder = new StringBuilder();
        foreach (var component in components)
        {
            builder.Append(Encoding.UTF8.GetByteCount(component).ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(component);
        }

        return builder.ToString();
    }

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static DateTimeOffset? ClaimableAt(WorkflowAlterationJobState job) => job.Status switch
    {
        WorkflowAlterationJobStatus.Pending => job.CreatedAt,
        WorkflowAlterationJobStatus.Running => job.Claim?.ExpiresAt,
        _ => null
    };

    private static string PhysicalId(string logicalId, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        if (logicalId.Length > ElsaRuntimeV2StorageManifest.IdMaximumLength)
            throw new ArgumentOutOfRangeException(nameof(logicalId), $"Groundwork alteration {kind} identities cannot exceed {ElsaRuntimeV2StorageManifest.IdMaximumLength} characters.");
        return logicalId;
    }

    private static T DeserializeContent<T>(IReadOnlyDictionary<string, object?> values, string rowKind)
    {
        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException($"Groundwork {rowKind} row content is not JSON.")
            }
            : throw new InvalidDataException($"Groundwork {rowKind} row did not contain JSON content.");
        try
        {
            return GroundworkV2RuntimeJson.Deserialize<T>(content)
                   ?? throw new InvalidDataException($"Groundwork {rowKind} row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Groundwork {rowKind} row content was not valid current JSON.", exception);
        }
    }

    private static void EnsureSchema(IReadOnlyDictionary<string, object?> values, string rowKind)
    {
        var schema = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schema, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException($"Groundwork {rowKind} row returned unsupported schema version '{schema}'.");
    }

    private static void EnsureString(IReadOnlyDictionary<string, object?> values, string field, string expected)
    {
        if (!StringComparer.Ordinal.Equals(RequiredString(values, field), expected))
            throw new InvalidDataException($"Groundwork alteration row projection '{field}' does not match its current content.");
    }

    private static void EnsureOptionalString(IReadOnlyDictionary<string, object?> values, string field, string? expected)
    {
        if (!values.TryGetValue(field, out var raw))
            throw new InvalidDataException($"Groundwork alteration row is missing projection '{field}'.");
        var actual = raw switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => throw new InvalidDataException($"Groundwork alteration row projection '{field}' is not a string.")
        };
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException($"Groundwork alteration row projection '{field}' does not match its current content.");
    }

    private static void EnsureInt64(IReadOnlyDictionary<string, object?> values, string field, long expected)
    {
        if (!values.TryGetValue(field, out var raw) || !TryReadInt64(raw, out var actual) || actual != expected)
            throw new InvalidDataException($"Groundwork alteration row projection '{field}' does not match its current content.");
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) switch
        {
            true when value is string text && !string.IsNullOrWhiteSpace(text) => text,
            true when value is JsonElement { ValueKind: JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            _ => throw new InvalidDataException($"Groundwork alteration row is missing required string field '{field}'.")
        };

    private static bool TryReadInt64(object? raw, out long value)
    {
        switch (raw)
        {
            case long number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var jsonNumber):
                value = jsonNumber;
                return true;
            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private static bool TryReadDateTime(object? raw, out DateTimeOffset? value)
    {
        if (raw is null || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            value = null;
            return true;
        }

        DateTimeOffset parsed = default;
        var success = raw switch
        {
            DateTimeOffset dateTimeOffset => (parsed = dateTimeOffset) == dateTimeOffset,
            DateTime dateTime => DateTimeOffset.TryParse(dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed),
            string text => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed),
            JsonElement { ValueKind: JsonValueKind.String } element => element.TryGetDateTimeOffset(out parsed),
            _ => false
        };
        value = success ? parsed : null;
        return success;
    }
}

internal sealed record WorkflowAlterationPlanDocument(
    string Collection,
    string PlanId,
    string ActiveOrderKey,
    WorkflowAlterationPlanState Plan,
    WorkflowAlterationUnsealedCaptureCleanup? UnsealedCaptureCleanup = null);

internal sealed record WorkflowAlterationUnsealedCaptureCleanup(
    WorkflowAlterationPlanStatus TerminalStatus,
    WorkflowAlterationSafeFailure? SafeFailure,
    DateTimeOffset CompletedAt,
    long DeletedCount = 0);

internal sealed record WorkflowAlterationJobDocument(
    string Collection,
    string JobId,
    string PlanId,
    long CaptureOrdinal,
    DateTimeOffset? ClaimableAt,
    WorkflowAlterationJobState Job);
