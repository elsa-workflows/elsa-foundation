using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    public ValueTask<JobContinuationResult> GrantContinuationAsync(JobContinuationRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Name(request.ResultId); JobStoreCodec.Name(request.ContinuationId); JobStoreCodec.Name(request.ModelInputId);
        return Write("grant-continuation", async (db, now, ct) =>
        {
            var job = await Find(db, request.Scope, request.JobId, ct);
            if (job is null) return new JobContinuationResult(JobCommandStatus.NotFound, null);
            var delivery = await db.Deliveries.SingleOrDefaultAsync(x => x.JobKey == job.Key, ct);
            if (delivery is null) return new JobContinuationResult(JobCommandStatus.NotFound, null);
            JobStoreCodec.Exact(delivery, request.Scope, "delivery", job.JobId);
            if (delivery.ResultId != request.ResultId) return new JobContinuationResult(JobCommandStatus.Conflict, null);
            if (delivery.Acknowledged) return new JobContinuationResult(JobCommandStatus.Terminal, null);
            if (delivery.PermissionJson is { } prior)
            {
                var permission = JobStoreCodec.ReadRetained<JobContinuationPermission>(prior);
                return new JobContinuationResult(permission.ContinuationId == request.ContinuationId && permission.ModelInputId == request.ModelInputId
                    ? JobCommandStatus.Replayed : JobCommandStatus.Conflict, permission.ContinuationId == request.ContinuationId && permission.ModelInputId == request.ModelInputId ? permission : null);
            }
            var gates = await Gates(db, request.Scope, JobStoreCodec.Gates(Request(job), workflowOnly: true), ct);
            if (await Availability(db, gates, now, permits: false, ct) is { } status) return new JobContinuationResult(status, null);
            Touch(gates);
            var grant = new JobContinuationPermission(request.ContinuationId, job.JobId, delivery.ResultId, request.ModelInputId,
                JobStoreCodec.ReadRetained<JobAssociation>(delivery.AssociationJson), JobStoreCodec.At(now));
            delivery.PermissionJson = JobStoreCodec.Json(grant);
            return new JobContinuationResult(JobCommandStatus.Applied, grant);
        }, cancellationToken);
    }

    public ValueTask<JobTransition> AcknowledgeDeliveryAsync(JobAcknowledgeDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Name(request.ContinuationId); JobStoreCodec.Name(request.Proof.ModelReceiptId); JobStoreCodec.Digest(request.Proof.ResultSha256);
        if (!Enum.IsDefined(request.Proof.Kind)) throw new ArgumentOutOfRangeException(nameof(request));
        var proof = JobStoreCodec.Json(new { request.ContinuationId, request.Proof });
        return Write("acknowledge-delivery", async (db, now, ct) =>
        {
            var job = await Find(db, request.Scope, request.JobId, ct);
            if (job is null) return Transition(JobCommandStatus.NotFound);
            var delivery = await db.Deliveries.SingleOrDefaultAsync(x => x.JobKey == job.Key, ct);
            if (delivery is null) return Transition(JobCommandStatus.NotFound, job);
            JobStoreCodec.Exact(delivery, request.Scope, "delivery", job.JobId);
            var metadata = RequestMetadata(job); var result = ResultMetadata(job, metadata) ?? throw InvalidRetention();
            _ = Retention(job, result);
            _ = await ReadDelivery(db, request.Scope, request.JobId, ct) ?? throw InvalidRetention();
            if (result.ResultId != delivery.ResultId || result.PayloadSha256 != request.Proof.ResultSha256) return Transition(JobCommandStatus.Conflict, job);
            if (delivery.Acknowledged) return Transition(delivery.ProofJson == proof ? JobCommandStatus.Replayed : JobCommandStatus.Conflict, job);
            if (job.InputExpiredAt is not null || job.ResultExpiredAt is not null) throw InvalidRetention();
            // The trusted owning model validates receipt meaning; the authority verifies exact retained result/input binding.
            if (request.Proof.Kind != JobDeliveryProofKind.ParentProvenTerminal)
            {
                if (delivery.PermissionJson is not { } permissionJson) return Transition(JobCommandStatus.Conflict, job);
                var permission = JobStoreCodec.ReadRetained<JobContinuationPermission>(permissionJson);
                if (permission.ContinuationId != request.ContinuationId) return Transition(JobCommandStatus.Conflict, job);
            }
            delivery.Acknowledged = true; delivery.ProofJson = proof;
            return Transition(JobCommandStatus.Applied, job);
        }, cancellationToken);
    }
}
