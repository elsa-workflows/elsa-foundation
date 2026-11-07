using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    private static JobRequestMetadata RequestMetadata(JobRecord job)
    {
        JobRequestMetadata? actual = job.RequestJson is not null ? JobRequestMetadata.From(Request(job)) : null;
        if (job.RequestMetadataJson is null)
        {
            // Legacy rows have no captured policy and remain indefinite. Missing bytes never become expiry.
            if (job.RequestMetadataSha256 is not null || job.TerminalInputRetentionTicks is not null || job.TerminalResultRetentionTicks is not null ||
                job.InputExpiredAt is not null || job.ResultExpiredAt is not null || actual is null) throw InvalidRetention();
            return actual;
        }
        if (job.RequestMetadataSha256 != JobStoreCodec.Hash(job.RequestMetadataJson)) throw InvalidRetention();
        var metadata = JobStoreCodec.ReadRetained<JobRequestMetadata>(job.RequestMetadataJson, JobStoreCodec.RequestMetadata);
        if (actual is not null && JobStoreCodec.Json(actual) != JobStoreCodec.Json(metadata) ||
            metadata.Handler.JobType != job.JobType || metadata.Queue != job.Queue ||
            JobStoreCodec.Support(new JobHandlerSupport(metadata.Handler, metadata.InputSchema, metadata.ResultSchema, string.Empty)) != job.HandlerKey)
            throw InvalidRetention();
        return metadata;
    }

    private static JobResultMetadata? ResultMetadata(JobRecord job, JobRequestMetadata request)
    {
        try { return ReadResultMetadata(job, request); }
        catch (Exception error) when (error is ArgumentException or OverflowException) { throw InvalidRetention(error); }
    }

    private static JobResultMetadata? ReadResultMetadata(JobRecord job, JobRequestMetadata request)
    {
        JobResult? actual = null;
        if (job.ResultJson is { } json)
        {
            actual = JobStoreCodec.ReadRetained<JobResult>(json);
            if (!Enum.IsDefined(actual.Outcome) || actual.JobId != job.JobId || actual.PayloadSha256 != JobPayloadDigest.Compute(actual.Payload) ||
                actual.Outcome == JobOutcome.Succeeded && (actual.Payload is null || actual.Payload.Schema != request.ResultSchema) ||
                actual.Outcome != JobOutcome.Succeeded && actual.Payload is not null) throw InvalidRetention();
            if (actual.Payload is { } payload) JobStoreCodec.Payload(payload);
            JobStoreCodec.Diagnostic(actual.Diagnostic);
        }
        if (job.ResultMetadataJson is null)
        {
            if (job.ResultMetadataSha256 is not null || job.ResultExpiredAt is not null) throw InvalidRetention();
            return actual is null ? null : JobResultMetadata.From(actual);
        }
        if (job.ResultMetadataSha256 != JobStoreCodec.Hash(job.ResultMetadataJson)) throw InvalidRetention();
        var metadata = JobStoreCodec.ReadRetained<JobResultMetadata>(job.ResultMetadataJson);
        JobStoreCodec.Name(metadata.ResultId); JobStoreCodec.Name(metadata.JobId); JobStoreCodec.Digest(metadata.PayloadSha256);
        if (metadata.AttemptId is not null) JobStoreCodec.Name(metadata.AttemptId);
        JobStoreCodec.Diagnostic(metadata.Diagnostic);
        if (!Enum.IsDefined(metadata.Outcome) || metadata.JobId != job.JobId ||
            metadata.Outcome == JobOutcome.Succeeded && metadata.PayloadSchema != request.ResultSchema ||
            metadata.Outcome != JobOutcome.Succeeded && (metadata.PayloadSchema is not null || metadata.PayloadSha256 != JobPayloadDigest.Compute(null)) ||
            actual is not null && JobStoreCodec.Json(JobResultMetadata.From(actual)) != JobStoreCodec.Json(metadata)) throw InvalidRetention();
        return metadata;
    }

    private static JobPayloadRetention Retention(JobRecord job, JobResultMetadata? result)
    {
        if (job.RetentionPolicySha256 is null
            ? job.TerminalInputRetentionTicks is not null || job.TerminalResultRetentionTicks is not null
            : job.RetentionPolicySha256 != RetentionPolicyDigest(job)) throw InvalidRetention();
        static TimeSpan? Duration(long? ticks) => ticks is null ? null : ticks > 0 && ticks <= TimeSpan.FromDays(36500).Ticks
            ? TimeSpan.FromTicks(ticks.Value) : throw InvalidRetention();
        var input = Duration(job.TerminalInputRetentionTicks); var output = Duration(job.TerminalResultRetentionTicks);
        var terminal = job.State == (int)JobState.Terminal;
        if (terminal != (result is not null)) throw InvalidRetention();
        long? inputDueAt = terminal && input is not null ? Expiry(result!.CompletedAt.UtcTicks, input.Value.Ticks) : null;
        long? outputDueAt = terminal && output is not null ? Expiry(result!.CompletedAt.UtcTicks, output.Value.Ticks) : null;
        if (job.InputExpiresAt != inputDueAt || job.ResultExpiresAt != outputDueAt ||
            job.InputExpiredAt is { } inputAt && (job.InputExpiresAt is not { } inputDue || inputAt < inputDue || job.RequestJson is not null) ||
            job.ResultExpiredAt is { } outputAt && (job.ResultExpiresAt is not { } outputDue || outputAt < outputDue || job.ResultJson is not null || job.ProgressJson is not null) ||
            job.InputExpiredAt is null && job.RequestJson is null || terminal && job.ResultExpiredAt is null && job.ResultJson is null)
            throw InvalidRetention();
        return new(input, output, At(job.InputExpiresAt), At(job.ResultExpiresAt), At(job.InputExpiredAt), At(job.ResultExpiredAt));
    }

    private static long Expiry(long completedAt, long duration)
    {
        try { var value = checked(completedAt + duration); _ = JobStoreCodec.At(value); return value; }
        catch (Exception error) when (error is ArgumentException or OverflowException) { throw InvalidRetention(error); }
    }
    private static DateTimeOffset? At(long? ticks)
    {
        try { return ticks is null ? null : JobStoreCodec.At(ticks.Value); }
        catch (ArgumentException error) { throw InvalidRetention(error); }
    }
    private static string RetentionPolicyDigest(JobRecord job) => JobStoreCodec.Hash(JobStoreCodec.Json(new
        { job.RequestSha256, job.TerminalInputRetentionTicks, job.TerminalResultRetentionTicks }));
    private static InvalidDataException InvalidRetention(Exception? inner = null) => new("The retained Jobs payload identity or expiry proof is unavailable.", inner);

    public ValueTask<JobPayloadCleanupResult> ExpirePayloadsAsync(JobPayloadCleanupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = new JobPageRequest(request.MaximumJobs, request.ContinuationToken);
        var after = After(request.Scope, "payload-cleanup", "", page); var scopeKey = JobStoreCodec.Scope(request.Scope);
        return Write("expire-payloads", async (db, now, ct) =>
        {
            var query = db.Jobs.Where(job => job.ScopeKey == scopeKey && job.State == (int)JobState.Terminal &&
                (job.InputExpiredAt == null && job.InputExpiresAt <= now || job.ResultExpiredAt == null && job.ResultExpiresAt <= now));
            if (after is not null) query = query.Where(job => string.Compare(job.Key, after) > 0);
            var candidates = await query.OrderBy(job => job.Key).Take(request.MaximumJobs + 1).ToArrayAsync(ct);
            var inputs = 0; var outputs = 0;
            foreach (var job in candidates.Take(request.MaximumJobs))
            {
                // Validate every protected value before changing any byte. Corrupt/missing proof is unavailable, never an expiry waiver.
                JobStoreCodec.Exact(job, request.Scope, "job", job.JobId);
                var metadata = RequestMetadata(job); var result = ResultMetadata(job, metadata) ?? throw InvalidRetention();
                _ = Retention(job, result);
                if (job.ActiveAttemptKey is not null) continue;
                var delivery = await ReadDelivery(db, request.Scope, job.JobId, ct);
                if (metadata.Association is { Mode: JobAssociationMode.Awaited, Continuation: not null } && delivery is null) throw InvalidRetention();
                if (metadata.Association is not { Mode: JobAssociationMode.Awaited, Continuation: not null } && delivery is not null) throw InvalidRetention();
                if (delivery is not null && !delivery.IsAcknowledged) continue;
                await foreach (var receipt in db.Receipts.AsNoTracking().Where(row => row.JobKey == job.Key).AsAsyncEnumerable().WithCancellation(ct))
                {
                    _ = ReceiptInspection(request.Scope, job, receipt);
                    // Every finite-policy admission uses the metadata-capable writer. Missing compact
                    // identity is unavailable, rather than permission to invent it while deleting bytes.
                    if (receipt.MetadataJson is null || receipt.MetadataSha256 is null) throw InvalidRetention();
                }
                if (job.InputExpiredAt is null && job.InputExpiresAt <= now)
                {
                    job.RequestJson = null; job.InputExpiredAt = now; inputs++;
                }
                if (job.ResultExpiredAt is null && job.ResultExpiresAt <= now)
                {
                    var receiptToken = Guid.NewGuid().ToByteArray();
                    await db.Receipts.Where(row => row.JobKey == job.Key).ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.ReceiptJson, (string?)null)
                        .SetProperty(row => row.PayloadExpiredAt, (long?)now)
                        .SetProperty(row => row.Token, receiptToken), ct);
                    job.ResultJson = null; job.ProgressJson = null; job.ResultExpiredAt = now; outputs++;
                }
            }
            return new JobPayloadCleanupResult(Math.Min(candidates.Length, request.MaximumJobs), inputs, outputs,
                candidates.Length > request.MaximumJobs ? CursorFor(request.Scope, "payload-cleanup", "", candidates[request.MaximumJobs - 1].Key) : null);
        }, cancellationToken);
    }

    private static void SaveReceiptMetadata(JobPhysicalReceiptRecord row, JobPhysicalReceiptMetadata metadata)
    {
        row.MetadataJson = JobStoreCodec.Json(metadata); row.MetadataSha256 = JobStoreCodec.Hash(row.MetadataJson);
    }

    private static JobPhysicalReceiptInspection ReceiptInspection(JobScope scope, JobRecord job, JobPhysicalReceiptRecord row)
    {
        try { return ReadReceiptInspection(scope, job, row); }
        catch (Exception error) when (error is ArgumentException or OverflowException) { throw InvalidRetention(error); }
    }

    private static JobPhysicalReceiptInspection ReadReceiptInspection(JobScope scope, JobRecord job, JobPhysicalReceiptRecord row)
    {
        JobPhysicalReceipt? receipt = null; JobPhysicalReceiptMetadata? actual = null;
        if (row.ReceiptJson is { } json)
        {
            receipt = JobStoreCodec.ReadRetained<JobPhysicalReceipt>(json, JobStoreCodec.Receipt);
            if (JobStoreCodec.Hash(json) != row.ReceiptSha256) throw InvalidRetention();
            actual = new(receipt.ReceiptId, receipt.AttemptId, receipt.Owner, receipt.Fence, receipt.Outcome, receipt.Payload?.Schema,
                receipt.PayloadSha256, receipt.Diagnostic, receipt.Retryable, receipt.ReportedAt, row.ReceiptSha256);
        }
        JobPhysicalReceiptMetadata metadata;
        if (row.MetadataJson is null) metadata = actual ?? throw InvalidRetention();
        else
        {
            if (row.MetadataSha256 != JobStoreCodec.Hash(row.MetadataJson)) throw InvalidRetention();
            metadata = JobStoreCodec.ReadRetained<JobPhysicalReceiptMetadata>(row.MetadataJson);
            if (actual is not null && JobStoreCodec.Json(actual) != JobStoreCodec.Json(metadata)) throw InvalidRetention();
        }
        JobStoreCodec.Exact(row, scope, "receipt", metadata.ReceiptId);
        JobStoreCodec.Name(metadata.AttemptId); JobStoreCodec.Name(metadata.Owner); JobStoreCodec.Digest(metadata.PayloadSha256); JobStoreCodec.Digest(metadata.ReceiptSha256);
        if (metadata.PayloadSchema is { } schema) JobStoreCodec.Schema(schema);
        JobStoreCodec.Diagnostic(metadata.Diagnostic);
        if (row.JobKey != job.Key || row.AttemptKey != JobStoreCodec.Key(scope, "attempt", job.JobId, metadata.AttemptId) ||
            metadata.ReceiptSha256 != row.ReceiptSha256 || metadata.Fence < 1 || !Enum.IsDefined(metadata.Outcome) ||
            metadata.Outcome == JobAttemptOutcome.Succeeded && metadata.PayloadSchema is null ||
            metadata.PayloadSchema is null && metadata.PayloadSha256 != JobPayloadDigest.Compute(null) ||
            row.PayloadExpiredAt is { } expired && (job.ResultExpiredAt is not { } jobExpired || expired < jobExpired || receipt is not null) ||
            row.PayloadExpiredAt is null && receipt is null) throw InvalidRetention();
        return new(metadata, receipt, row.PayloadExpiredAt is null ? JobPayloadState.Available : JobPayloadState.Expired, At(row.PayloadExpiredAt));
    }
}
