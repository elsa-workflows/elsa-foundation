using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    public ValueTask<JobContinuationPermission?> FindContinuationAsync(JobScope scope, string jobId, string continuationId,
        CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Name(jobId); JobStoreCodec.Name(continuationId);
        return Read<JobContinuationPermission?>("find-continuation", async (db, ct) =>
        {
            var delivery = await ReadDelivery(db, scope, jobId, ct);
            return delivery?.Permission is { } permission && permission.ContinuationId == continuationId ? permission : null;
        }, cancellationToken);
    }

    public ValueTask<JobDeliveryStatusSnapshot?> FindDeliveryAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Name(jobId); _ = JobStoreCodec.Scope(scope);
        return Read<JobDeliveryStatusSnapshot?>("find-delivery", (db, ct) => ReadDelivery(db, scope, jobId, ct), cancellationToken);
    }

    private static async ValueTask<JobDeliveryStatusSnapshot?> ReadDelivery(JobsDbContext db, JobScope scope, string jobId, CancellationToken ct)
    {
        var jobKey = JobStoreCodec.Key(scope, "job", jobId); var deliveryKey = JobStoreCodec.Key(scope, "delivery", jobId);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Key == jobKey, ct);
        if (job is null) return null;
        JobStoreCodec.Exact(job, scope, "job", jobId);
        var delivery = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(x => x.Key == deliveryKey, ct);
        if (delivery is null) return null;
        JobStoreCodec.Exact(delivery, scope, "delivery", jobId);
        if (delivery.JobKey != jobKey) throw new InvalidDataException("The retained Jobs delivery has an invalid job binding.");
        var snapshot = await AvailableSnapshot(db, scope, job, ct);
        if (snapshot.State != JobState.Terminal || snapshot.Request?.Association is not { } association || snapshot.Result is not { } result
            || result.ResultId != delivery.ResultId || delivery.AssociationJson != JobStoreCodec.Json(association))
            throw new InvalidDataException("The retained Jobs delivery does not match its selected result and association.");
        JobContinuationPermission? permission = null;
        if (delivery.PermissionJson is { } json)
        {
            permission = JobStoreCodec.ReadRetained<JobContinuationPermission>(json, value =>
            {
                JobStoreCodec.Name(value.JobId); JobStoreCodec.Name(value.ResultId);
                JobStoreCodec.Name(value.ContinuationId); JobStoreCodec.Name(value.ModelInputId);
            });
            if (permission.JobId != jobId || permission.ResultId != result.ResultId
                || permission.Association is null || JobStoreCodec.Json(permission.Association) != JobStoreCodec.Json(association))
                throw new InvalidDataException("The retained Jobs continuation grant does not match its selected result and association.");
        }
        JobDeliveryAcknowledgement? acknowledgement = null;
        if (delivery.Acknowledged != (delivery.ProofJson is not null))
            throw new InvalidDataException("The retained Jobs delivery acknowledgement is incomplete.");
        if (delivery.ProofJson is { } proofJson)
        {
            acknowledgement = JobStoreCodec.ReadRetained<JobDeliveryAcknowledgement>(proofJson, value =>
            {
                JobStoreCodec.Name(value.ContinuationId); ArgumentNullException.ThrowIfNull(value.Proof);
                JobStoreCodec.Name(value.Proof.ModelReceiptId); JobStoreCodec.Digest(value.Proof.ResultSha256);
                if (!Enum.IsDefined(value.Proof.Kind)) throw new ArgumentException("The retained Jobs delivery proof kind is invalid.");
            });
            if (acknowledgement.Proof.ResultSha256 != result.PayloadSha256
                || acknowledgement.Proof.Kind != JobDeliveryProofKind.ParentProvenTerminal
                && (permission is null || permission.ContinuationId != acknowledgement.ContinuationId))
                throw new InvalidDataException("The retained Jobs delivery acknowledgement does not match its selected result and permission.");
        }
        return new(jobId, result.ResultId, association, permission, acknowledgement);
    }

    private static async ValueTask<JobSnapshot> AvailableSnapshot(JobsDbContext db, JobScope scope, JobRecord job, CancellationToken ct)
    {
        try
        {
            var snapshot = await Snapshot(db, scope, job, ct);
            if (snapshot.UnavailableDiagnostic is not null || snapshot.Request is null)
                throw new InvalidDataException("The retained Jobs invocation is unavailable.");
            return snapshot;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { throw new InvalidDataException("The retained Jobs invocation has invalid fields.", error); }
    }

    public ValueTask<JobOriginSnapshot?> FindOriginAsync(JobScope scope, JobOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ValidateOrigin(origin);
        var key = JobStoreCodec.Key(scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
        return Read<JobOriginSnapshot?>("find-origin", async (db, ct) =>
        {
            var row = await db.Origins.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
            if (row is null) return null;
            JobStoreCodec.Exact(row, scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
            try { JobStoreCodec.Digest(row.RequestSha256); JobStoreCodec.Digest(row.OriginSha256); }
            catch (ArgumentException error) { throw new InvalidDataException("The retained Jobs origin has an invalid digest.", error); }
            if (row.OriginSha256 != origin.PayloadSha256)
                throw new JobException("jobs.origin.conflict", "The retained Jobs sender address has a different immutable source digest.");
            JobAcceptance? acceptance = null;
            if (row.JobKey is { } jobKey)
            {
                var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Key == jobKey, ct)
                    ?? throw new InvalidDataException("The protected Jobs origin lost its accepted invocation.");
                JobStoreCodec.Exact(job, scope, "job", job.JobId);
                var snapshot = await AvailableSnapshot(db, scope, job, ct);
                if (job.Key != JobStoreCodec.Key(scope, "job", job.JobId) || job.RequestSha256 != row.RequestSha256)
                    throw new InvalidDataException("The protected Jobs origin has an invalid invocation binding.");
                try { acceptance = Acceptance(job, checked(job.AcceptedAt + snapshot.Request!.Policy.MinimumKeyRecognition.Ticks)); }
                catch (Exception error) when (error is ArgumentException or OverflowException)
                { throw new InvalidDataException("The retained Jobs acceptance has invalid fields.", error); }
            }
            else if (!row.Cancelled && !row.Retired)
                throw new InvalidDataException("The protected Jobs origin has neither an accepted invocation nor a tombstone.");
            return new(origin, row.RequestSha256, row.Cancelled, row.Retired, acceptance);
        }, cancellationToken);
    }
}
