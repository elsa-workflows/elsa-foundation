using System.Data.Common;
using Elsa.Jobs.Core;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore(IDbContextFactory<JobsDbContext> contexts, JobsStoreSettings settings,
    IJobRecurrenceCalculator? recurrence = null) : IJobCommands, IJobQueries
{
    private readonly EfWriteRetry _retry = new(settings.MaximumWriteAttempts,
        EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient,
        attempt => TimeSpan.FromMilliseconds(Math.Min(50, attempt * 2)));

    // Each retry creates and disposes its ENTIRE context/transaction. No provider transaction survives an attempt.
    private async ValueTask<T> Write<T>(string operation, Func<JobsDbContext, long, CancellationToken, ValueTask<T>> body, CancellationToken cancellationToken)
    {
        try
        {
            return await _retry.RunAsync<T>(null, async () =>
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                var result = await body(db, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                // Lease-authorized operations register the OLD persisted authority deadline, never their extension.
                if (_leaseWindows.TryGetValue(db, out var window))
                {
                    var after = await db.ReadUtcTicksAsync(cancellationToken);
                    if (after < now || after >= window)
                        throw new LeaseExpiredDuringWriteException();
                }
                await transaction.CommitAsync(cancellationToken);
                return result;
            }, error => ValueTask.FromException<T>(new JobPersistenceException(operation, error ?? new InvalidOperationException("Jobs write retry budget exhausted."))), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (JobException) { throw; }
        catch (LeaseExpiredDuringWriteException) { throw; }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        { throw new JobException("jobs.persistence.invalid-record", "The retained Jobs record is unavailable to this provider.", error); }
        catch (Exception error) when (error is DbException or DbUpdateException || EfRelationalExceptionClassifier.IsWrappedProviderFailure(error))
        { throw new JobPersistenceException(operation, error); }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<JobsDbContext, LeaseWindow> _leaseWindows = new();
    private sealed record LeaseWindow(long ExpiresAt)
    {
        public static implicit operator long(LeaseWindow window) => window.ExpiresAt;
    }
    private sealed class LeaseExpiredDuringWriteException : Exception { }
    private void AuthorizeUntil(JobsDbContext db, long expiresAt) => _leaseWindows.AddOrUpdate(db, new(expiresAt));
    private static JobTransition Transition(JobCommandStatus status, JobRecord? job = null) => new(status, job?.Revision, job?.JobId,
        job?.ResultJson is { } json ? JobStoreCodec.ReadRetained<JobResult>(json).ResultId : null);
    private static JobAcceptance Acceptance(JobRecord job, long recognizeUntil) => new(job.JobId, JobStoreCodec.At(job.AcceptedAt), JobStoreCodec.At(recognizeUntil), job.Revision);
    private static JobAttemptLease Lease(JobScope scope, JobRecord job, JobAttemptRecord attempt) => new(scope, job.JobId,
        attempt.AttemptId, attempt.Number, attempt.Owner, attempt.Fence, attempt.OwnershipRevision,
        JobStoreCodec.At(attempt.GrantedAt), JobStoreCodec.At(attempt.ExpiresAt), JobStoreCodec.At(attempt.Deadline), attempt.ImplementationIdentity);
    private static JobRequest Request(JobRecord job)
    {
        try
        {
            // Constructor validation can throw ArgumentException during deserialization itself.
            // This boundary reads retained data only; ordinary caller validation remains separate.
            var request = JobStoreCodec.ReadRetained<JobRequest>(job.RequestJson);
            JobStoreCodec.Request(request);
            if (JobRequestDigest.Compute(request) != job.RequestSha256) throw new InvalidDataException("The retained Jobs request digest does not match.");
            return request;
        }
        catch (Exception error) when (error is ArgumentException || JobStoreCodec.SerializationFailure(error))
        { throw new InvalidDataException("The retained Jobs request has invalid bounded fields.", error); }
    }
    private static async ValueTask<JobRecord?> Find(JobsDbContext db, JobScope scope, string jobId, CancellationToken ct)
    {
        JobStoreCodec.Name(jobId);
        var key = JobStoreCodec.Key(scope, "job", jobId);
        var job = await db.Jobs.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (job is not null) JobStoreCodec.Exact(job, scope, "job", jobId);
        return job;
    }
    private static async ValueTask<JobGateRecord[]> Gates(JobsDbContext db, JobScope scope, IEnumerable<JobGateKey> keys, CancellationToken ct)
    {
        var result = new List<JobGateRecord>();
        foreach (var gate in keys.OrderBy(x => x.Kind).ThenBy(x => x.ModelKind, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            JobStoreCodec.Gate(gate);
            var key = JobStoreCodec.Key(scope, "gate", gate.Kind, gate.Name, gate.ModelKind);
            var row = db.Gates.Local.SingleOrDefault(x => x.Key == key) ?? await db.Gates.SingleOrDefaultAsync(x => x.Key == key, ct);
            if (row is null)
            {
                row = JobStoreCodec.New<JobGateRecord>(scope, "gate", gate.Kind, gate.Name, gate.ModelKind);
                row.Kind = (int)gate.Kind; row.Name = gate.Name; row.ModelKind = gate.ModelKind;
                db.Gates.Add(row);
            }
            else JobStoreCodec.Exact(row, scope, "gate", gate.Kind, gate.Name, gate.ModelKind);
            result.Add(row);
        }
        return result.ToArray();
    }
    private static void Touch(IEnumerable<JobGateRecord> gates)
    {
        foreach (var gate in gates) gate.ArbitrationRevision = checked(gate.ArbitrationRevision + 1);
    }
    private async ValueTask<JobCommandStatus?> Availability(JobsDbContext db, JobGateRecord[] gates, long now, bool permits, CancellationToken ct)
    {
        if (gates.Any(x => x.ActiveHolds > 0)) return JobCommandStatus.Held;
        if (permits)
            foreach (var gate in gates.Where(x => x.Capacity is not null))
                if (await db.Permits.CountAsync(x => x.GateKey == gate.Key && x.ExpiresAt > now, ct) >= gate.Capacity!.Value)
                    return JobCommandStatus.CapacityUnavailable;
        return null;
    }
    private static bool Owns(JobRecord job, JobAttemptRecord attempt, JobAttemptLease lease, long now) =>
        job.ActiveAttemptKey == attempt.Key && job.State == (int)JobState.AttemptActive && attempt.Owner == lease.Owner
        && attempt.Fence == lease.Fence && attempt.OwnershipRevision == lease.Revision && attempt.ExpiresAt > now
        && attempt.ImplementationIdentity == lease.ImplementationIdentity && attempt.AttemptId == lease.AttemptId && attempt.Number == lease.AttemptNumber;
    private static async ValueTask<JobAttemptRecord?> Attempt(JobsDbContext db, JobScope scope, JobRecord job, string attemptId, CancellationToken ct)
    {
        var key = JobStoreCodec.Key(scope, "attempt", job.JobId, attemptId);
        var attempt = await db.Attempts.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (attempt is not null) JobStoreCodec.Exact(attempt, scope, "attempt", job.JobId, attemptId);
        return attempt;
    }
    private static async ValueTask ReleasePermits(JobsDbContext db, JobAttemptRecord attempt, CancellationToken ct)
    {
        db.Permits.RemoveRange(await db.Permits.Where(x => x.AttemptKey == attempt.Key).ToArrayAsync(ct));
    }

    private async ValueTask<T?> Replay<T>(JobsDbContext db, JobScope scope, JobOperation operation, string digest, CancellationToken ct) where T : class
    {
        JobStoreCodec.Operation(operation);
        var row = await db.Operations.SingleOrDefaultAsync(x => x.Key == JobStoreCodec.Key(scope, "operation", operation.OperationId), ct);
        if (row is null) return null;
        JobStoreCodec.Exact(row, scope, "operation", operation.OperationId);
        if (row.Digest != digest) throw new JobException("jobs.operation.conflict", "The operation identity recognizes different immutable content.");
        return JobStoreCodec.ReadRetained<T>(row.ResultJson);
    }
    private static void Remember<T>(JobsDbContext db, JobScope scope, JobOperation operation, string digest, T result,
        JobOperationKind kind, long now, JobOperationTarget target)
    {
        var row = JobStoreCodec.New<JobOperationRecord>(scope, "operation", operation.OperationId);
        row.Digest = digest; row.ResultJson = JobStoreCodec.Json(result);
        row.AuditJson = WriteAudit(new(1, scope, operation, kind, JobStoreCodec.At(now), digest, target, OperationConfirmation(result)));
        db.Operations.Add(row);
    }
}
