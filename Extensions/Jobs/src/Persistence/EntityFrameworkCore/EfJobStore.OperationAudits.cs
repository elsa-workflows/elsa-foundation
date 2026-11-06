using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    private const int MaximumAuditBytes = 32 * 1024;
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    private static JobTransition OperationConfirmation<T>(T result) => result switch
    {
        JobTransition transition => transition,
        JobAdmissionResult admission => new(admission.Status, admission.Acceptance?.Revision, admission.Acceptance?.JobId, null),
        _ => throw new InvalidOperationException("The Jobs operation receipt has no supported confirmation shape.")
    };

    private static string WriteAudit(JobOperationAudit audit)
    {
        ValidateAudit(audit);
        var json = JsonSerializer.Serialize(audit, AuditJsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumAuditBytes) throw new ArgumentException("The Jobs operation audit exceeds its bounded size.");
        return json;
    }

    private static void ValidateAudit(JobOperationAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.FormatVersion != 1 || !Enum.IsDefined(audit.Kind)) throw new NotSupportedException("The Jobs operation audit schema is unavailable.");
        _ = JobStoreCodec.Scope(audit.Scope); ArgumentNullException.ThrowIfNull(audit.Operation);
        JobStoreCodec.Operation(audit.Operation); JobStoreCodec.Digest(audit.CommandSha256);
        if (audit.RecordedAt.Offset != TimeSpan.Zero) throw new ArgumentException("The Jobs operation audit time must be UTC.");
        ArgumentNullException.ThrowIfNull(audit.Target); ArgumentNullException.ThrowIfNull(audit.Confirmation);
        var target = audit.Target; var kind = audit.Kind;
        if ((kind is JobOperationKind.Cancel or JobOperationKind.RetryAsNewRun) != (target.JobId is not null) ||
            (kind == JobOperationKind.CancelOrigin) != (target.Origin is not null) ||
            (kind is JobOperationKind.Pause or JobOperationKind.ReleaseHold or JobOperationKind.SetCapacity) != (target.Gate is not null) ||
            (kind is JobOperationKind.Pause or JobOperationKind.ReleaseHold) != (target.HoldId is not null) ||
            (kind is JobOperationKind.SaveSchedule or JobOperationKind.SetScheduleEnabled) != (target.ScheduleId is not null) ||
            (kind is JobOperationKind.RetryAsNewRun or JobOperationKind.ReleaseHold or JobOperationKind.SetCapacity or JobOperationKind.SaveSchedule or JobOperationKind.SetScheduleEnabled) != (target.ExpectedRevision is not null) ||
            (kind == JobOperationKind.SetScheduleEnabled) != (target.Enabled is not null) ||
            kind != JobOperationKind.SetCapacity && target.CapacityLimit is not null || target.CapacityLimit < 0)
            throw new ArgumentException("The Jobs operation audit has invalid command targets.");
        if (target.JobId is { } jobId) JobStoreCodec.Name(jobId);
        if (target.Origin is { } origin) ValidateOrigin(origin);
        if (target.Gate is { } gate) JobStoreCodec.Gate(gate);
        if (target.HoldId is { } holdId) JobStoreCodec.Name(holdId);
        if (target.ScheduleId is { } scheduleId) JobStoreCodec.Name(scheduleId);
        var confirmation = audit.Confirmation;
        if (!Enum.IsDefined(confirmation.Status) || confirmation.Revision < 0) throw new ArgumentException("The Jobs operation audit confirmation is invalid.");
        if (confirmation.JobId is { } selectedJobId) JobStoreCodec.Name(selectedJobId);
        if (confirmation.ResultId is { } resultId) JobStoreCodec.Name(resultId);
    }

    private static string OperationId(JobScope scope, JobOperationRecord row)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(row.Identity);
            if (row.Identity.Length > 8192 || Encoding.UTF8.GetByteCount(row.Identity) > 8192)
                throw new InvalidDataException("The retained Jobs operation identity exceeds its bound.");
            using var json = JsonDocument.Parse(row.Identity, new JsonDocumentOptions { MaxDepth = 8 });
            var frame = json.RootElement;
            if (frame.ValueKind != JsonValueKind.Array || frame.GetArrayLength() != 4 || frame[0].ValueKind != JsonValueKind.String ||
                frame[1].ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || frame[2].ValueKind != JsonValueKind.String ||
                frame[3].ValueKind != JsonValueKind.Array || frame[3].GetArrayLength() != 1 || frame[3][0].ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The retained Jobs operation identity has an invalid frame.");
            var operationId = frame[3][0].GetString()!; JobStoreCodec.Name(operationId);
            JobStoreCodec.ExactIdentity(row, scope, "operation", operationId);
            if (row.Key != JobStoreCodec.Key(scope, "operation", operationId)) throw new InvalidDataException("The retained Jobs operation identity differs from its address.");
            return operationId;
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { throw new InvalidDataException("The retained Jobs operation identity is invalid.", error); }
    }

    private static JobOperationAuditSnapshot AuditSnapshot(JobScope scope, JobOperationRecord row)
    {
        var operationId = OperationId(scope, row);
        if (row.SchemaVersion != JobsEfModule.Version) return new(operationId, null,
            new("jobs.audit.schema-unavailable", "The retained operation audit schema is unavailable; its original receipt remains protected."));
        if (row.AuditJson is null) return new(operationId, null,
            new("jobs.audit.legacy-unavailable", "This receipt has no retained audit metadata; its actor, reason and recorded time are unavailable."));
        try
        {
            if (row.AuditJson.Length > MaximumAuditBytes || Encoding.UTF8.GetByteCount(row.AuditJson) > MaximumAuditBytes)
                throw new InvalidDataException("The retained Jobs operation audit exceeds its bound.");
            using var document = JsonDocument.Parse(row.AuditJson, new JsonDocumentOptions { MaxDepth = 16 });
            var audit = document.RootElement.Deserialize<JobOperationAudit>(AuditJsonOptions)
                ?? throw new InvalidDataException("The retained Jobs operation audit is null.");
            ValidateAudit(audit); JobStoreCodec.Digest(row.Digest);
            // Null is the explicit unlimited policy, not a default for a lost optional JSON field.
            if (audit.Kind == JobOperationKind.SetCapacity && (!document.RootElement.TryGetProperty("Target", out var targetJson) ||
                targetJson.ValueKind != JsonValueKind.Object || !targetJson.TryGetProperty("CapacityLimit", out _)))
                throw new InvalidDataException("The retained Jobs capacity audit lost its explicit limit decision.");
            if (audit.Scope != scope || audit.Operation.OperationId != operationId || audit.CommandSha256 != row.Digest)
                throw new InvalidDataException("The retained Jobs operation audit has an invalid receipt binding.");
            ArgumentNullException.ThrowIfNull(row.ResultJson);
            if (row.ResultJson.Length > 8192 || Encoding.UTF8.GetByteCount(row.ResultJson) > 8192)
                throw new InvalidDataException("The retained Jobs operation confirmation exceeds its bound.");
            var confirmation = audit.Kind == JobOperationKind.RetryAsNewRun
                ? OperationConfirmation(JsonSerializer.Deserialize<JobAdmissionResult>(row.ResultJson, AuditJsonOptions)
                    ?? throw new InvalidDataException("The retained Jobs operation confirmation is null."))
                : (JsonSerializer.Deserialize<JobTransition>(row.ResultJson, AuditJsonOptions)
                    ?? throw new InvalidDataException("The retained Jobs operation confirmation is null."));
            if (audit.Confirmation != confirmation) throw new InvalidDataException("The retained Jobs operation audit differs from its original confirmation.");
            return new(operationId, audit);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException or OverflowException || JobStoreCodec.SerializationFailure(error))
        {
            return new(operationId, null, new("jobs.audit.record-unavailable",
                "The retained operation audit metadata is unavailable; its original receipt and bytes remain protected."));
        }
    }

    public ValueTask<JobOperationAuditSnapshot?> FindOperationAuditAsync(JobScope scope, string operationId, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Name(operationId); _ = JobStoreCodec.Scope(scope);
        return Read<JobOperationAuditSnapshot?>("find-operation-audit", async (db, ct) =>
        {
            var key = JobStoreCodec.Key(scope, "operation", operationId);
            var row = await db.Operations.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, ct);
            return row is null ? null : AuditSnapshot(scope, row);
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobOperationAuditSnapshot>> ListOperationAuditsAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "operation-audits", "", page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-operation-audits", async (db, ct) =>
        {
            var rows = db.Operations.AsNoTracking().Where(item => item.ScopeKey == scopeKey);
            if (after is not null) rows = rows.Where(item => string.Compare(item.Key, after) > 0);
            var found = await rows.OrderBy(item => item.Key).Take(page.Limit + 1).ToArrayAsync(ct);
            return new JobPage<JobOperationAuditSnapshot>(found.Take(page.Limit).Select(item => AuditSnapshot(scope, item)).ToArray(),
                found.Length > page.Limit ? CursorFor(scope, "operation-audits", "", found[page.Limit - 1].Key) : null);
        }, cancellationToken);
    }
}
