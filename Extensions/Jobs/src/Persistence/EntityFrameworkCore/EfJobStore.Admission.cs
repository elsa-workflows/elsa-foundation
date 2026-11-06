using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    public ValueTask<JobAdmissionResult> AdmitAsync(JobAdmission admission, CancellationToken cancellationToken = default)
    {
        // Snapshot before the first await. The records may contain caller-owned JSON and policies.
        var frozen = JobStoreCodec.Preflight(() =>
        {
            ValidateAdmission(admission);
            return JobStoreCodec.Read<JobAdmission>(JobStoreCodec.Json(admission));
        }, "jobs.request.invalid");
        return Write("admit", (db, now, ct) => Admit(db, frozen, now, ct), cancellationToken);
    }

    private static void ValidateAdmission(JobAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        _ = JobStoreCodec.Scope(admission.Scope);
        JobStoreCodec.Name(admission.ProposedJobId); JobStoreCodec.Name(admission.IdempotencyKey);
        JobStoreCodec.Request(admission.Request); JobStoreCodec.Digest(admission.RequestSha256);
        if (admission.RequestSha256 != JobRequestDigest.Compute(admission.Request))
            throw new ArgumentException("The Jobs admission request digest does not match its immutable request.");
        if (admission.Origin is { } origin) ValidateOrigin(origin);
    }

    private static void ValidateOrigin(JobOrigin origin)
    {
        JobStoreCodec.Name(origin.Kind); JobStoreCodec.Name(origin.OwnerId); JobStoreCodec.Name(origin.OriginId); JobStoreCodec.Digest(origin.PayloadSha256);
    }

    private async ValueTask<JobAdmissionResult> Admit(JobsDbContext db, JobAdmission admission, long now, CancellationToken ct, string? retriedFrom = null)
    {
        var scope = admission.Scope;
        JobOriginRecord? originRow = null;
        if (admission.Origin is { } origin)
        {
            var originKey = JobStoreCodec.Key(scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
            originRow = await db.Origins.SingleOrDefaultAsync(x => x.Key == originKey, ct);
            if (originRow is not null)
            {
                JobStoreCodec.Exact(originRow, scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
                if (originRow.RequestSha256 != admission.RequestSha256 || originRow.OriginSha256 != origin.PayloadSha256)
                    return new(JobCommandStatus.Conflict, null);
                if (originRow.Cancelled) return new(JobCommandStatus.OriginCancelled, null);
                if (originRow.Retired) return new(JobCommandStatus.OriginRetired, null);
                var recognized = await db.Jobs.SingleOrDefaultAsync(x => x.Key == originRow.JobKey, ct)
                    ?? throw new InvalidDataException("A protected Jobs origin lost its accepted invocation.");
                JobStoreCodec.Exact(recognized, scope, "job", recognized.JobId);
                if (recognized.RequestSha256 != admission.RequestSha256) throw new InvalidDataException("The Jobs origin request identity diverged.");
                return new(JobCommandStatus.Replayed, Acceptance(recognized, checked(recognized.AcceptedAt + Request(recognized).Policy.MinimumKeyRecognition.Ticks)));
            }
        }
        var recognitionKey = JobStoreCodec.Key(scope, "recognition", admission.IdempotencyKey);
        var recognition = await db.Recognition.SingleOrDefaultAsync(x => x.Key == recognitionKey, ct);
        JobRecord? prior = null;
        if (recognition is not null)
        {
            JobStoreCodec.Exact(recognition, scope, "recognition", admission.IdempotencyKey);
            prior = await db.Jobs.SingleOrDefaultAsync(x => x.Key == recognition.JobKey, ct)
                ?? throw new InvalidDataException("A protected Jobs producer key lost its invocation.");
            JobStoreCodec.Exact(prior, scope, "job", prior.JobId);
            // Expiry does not itself erase recognition. Pending job/delivery responsibility always protects it.
            var protectedKey = recognition.RecognizeUntil > now || prior.State != (int)JobState.Terminal
                || await db.Deliveries.AnyAsync(x => x.JobKey == prior.Key && !x.Acknowledged, ct);
            if (protectedKey)
            {
                if (recognition.RequestSha256 != admission.RequestSha256 || prior.RequestSha256 != admission.RequestSha256 || prior.RetriedFromJobId != retriedFrom)
                    return new(JobCommandStatus.Conflict, null);
                if (admission.Origin is not null)
                {
                    // Bind a newly supplied trusted origin to the existing winner atomically too.
                    originRow = NewOrigin(admission);
                    originRow.JobKey = prior.Key; db.Origins.Add(originRow);
                }
                return new(JobCommandStatus.Replayed, Acceptance(prior, recognition.RecognizeUntil));
            }
            // Explicit admission cleanup retires only this expired ordinary key; the old JobId and origins remain.
            db.Recognition.Remove(recognition);
        }
        var jobKey = JobStoreCodec.Key(scope, "job", admission.ProposedJobId);
        var existingId = await db.Jobs.AnyAsync(x => x.Key == jobKey, ct);
        if (existingId) return new(JobCommandStatus.Conflict, null);
        var job = JobStoreCodec.New<JobRecord>(scope, "job", admission.ProposedJobId);
        job.JobId = admission.ProposedJobId;
        job.RequestJson = JobStoreCodec.Json(admission.Request); job.RequestSha256 = admission.RequestSha256;
        job.HandlerKey = JobStoreCodec.Support(admission.Request); job.JobType = admission.Request.Handler.JobType; job.Queue = admission.Request.Queue;
        job.State = (int)JobState.Accepted; job.Revision = 1; job.AcceptedAt = now;
        job.EligibleAt = Math.Max(now, admission.Request.NotBefore?.UtcTicks ?? now); job.RetriedFromJobId = retriedFrom;
        db.Jobs.Add(job);
        var until = checked(now + admission.Request.Policy.MinimumKeyRecognition.Ticks);
        if (recognition is null || db.Entry(recognition).State == EntityState.Deleted)
        {
            if (recognition is not null)
            {
                // Replace in place so the unique address never has two tracked entities and a later conflict rolls back all changes.
                db.Entry(recognition).State = EntityState.Modified;
                recognition.JobKey = job.Key; recognition.RequestSha256 = admission.RequestSha256; recognition.RecognizeUntil = until;
            }
            else
            {
                recognition = JobStoreCodec.New<JobRecognitionRecord>(scope, "recognition", admission.IdempotencyKey);
                recognition.JobKey = job.Key; recognition.RequestSha256 = admission.RequestSha256; recognition.RecognizeUntil = until;
                db.Recognition.Add(recognition);
            }
        }
        if (admission.Origin is not null)
        {
            originRow = NewOrigin(admission); originRow.JobKey = job.Key; db.Origins.Add(originRow);
        }
        foreach (var gate in await Gates(db, scope, JobStoreCodec.Gates(admission.Request), ct))
        {
            var membership = JobStoreCodec.New<JobGateMembershipRecord>(scope, "membership", job.JobId, gate.Key);
            membership.JobKey = job.Key; membership.GateKey = gate.Key; db.Memberships.Add(membership);
        }
        return new(JobCommandStatus.Applied, Acceptance(job, until));
    }

    private static JobOriginRecord NewOrigin(JobAdmission admission)
    {
        var origin = admission.Origin!;
        var row = JobStoreCodec.New<JobOriginRecord>(admission.Scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
        row.RequestSha256 = admission.RequestSha256; row.OriginSha256 = origin.PayloadSha256;
        return row;
    }

    public ValueTask<JobTransition> CancelAsync(JobCancelRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Operation(request.Operation);
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "cancel", request }));
        return Write("cancel", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var job = await Find(db, request.Scope, request.JobId, ct);
            var result = job is null ? Transition(JobCommandStatus.NotFound) : await Cancel(db, request.Scope, job, now, ct);
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.Cancel, now, new(JobId: request.JobId));
            return result;
        }, cancellationToken);
    }

    private async ValueTask<JobTransition> Cancel(JobsDbContext db, JobScope scope, JobRecord job, long now, CancellationToken ct)
    {
        if (job.State == (int)JobState.Terminal) return Transition(JobCommandStatus.Terminal, job);
        if (job.CancellationRequested) return Transition(JobCommandStatus.Replayed, job);
        job.CancellationRequested = true; job.Revision = checked(job.Revision + 1);
        var active = job.ActiveAttemptKey is { } key ? await db.Attempts.SingleOrDefaultAsync(x => x.Key == key, ct) : null;
        if (active is null || active.ExpiresAt <= now)
        {
            if (active is not null)
            {
                active.State = (int)JobAttemptState.OwnershipLost;
                await ReleasePermits(db, active, ct);
            }
            await Settle(db, scope, job, active, JobOutcome.Cancelled, null,
                new("jobs.cancelled", "The job was cancelled before a live attempt could complete."), now, ct);
        }
        return Transition(JobCommandStatus.Applied, job);
    }

    public ValueTask<JobTransition> CancelOriginAsync(JobCancelOriginRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOrigin(request.Origin); JobStoreCodec.Digest(request.RequestSha256); JobStoreCodec.Operation(request.Operation);
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "cancel-origin", request }));
        return Write("cancel-origin", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var origin = request.Origin;
            var key = JobStoreCodec.Key(request.Scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
            var row = await db.Origins.SingleOrDefaultAsync(x => x.Key == key, ct);
            JobTransition result;
            if (row is null)
            {
                row = JobStoreCodec.New<JobOriginRecord>(request.Scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
                row.OriginSha256 = origin.PayloadSha256; row.RequestSha256 = request.RequestSha256; row.Cancelled = true;
                db.Origins.Add(row); result = Transition(JobCommandStatus.Applied);
            }
            else
            {
                JobStoreCodec.Exact(row, request.Scope, "origin", origin.Kind, origin.OwnerId, origin.OriginId);
                if (row.RequestSha256 != request.RequestSha256 || row.OriginSha256 != origin.PayloadSha256) result = Transition(JobCommandStatus.Conflict);
                else if (row.Cancelled) result = Transition(JobCommandStatus.Replayed);
                else
                {
                    row.Cancelled = true;
                    var job = row.JobKey is { } jobKey ? await db.Jobs.SingleOrDefaultAsync(x => x.Key == jobKey, ct) : null;
                    result = job is null ? Transition(JobCommandStatus.OriginCancelled) : await Cancel(db, request.Scope, job, now, ct);
                }
            }
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.CancelOrigin, now, new(Origin: request.Origin)); return result;
        }, cancellationToken);
    }

    public ValueTask<JobAdmissionResult> RetryAsNewRunAsync(JobRetryAsNewRunRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Operation(request.Operation); JobStoreCodec.Name(request.ProposedNewJobId); JobStoreCodec.Name(request.IdempotencyKey);
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "retry-new", request.Scope, request.JobId, request.ExpectedTerminalRevision, request.IdempotencyKey, request.Operation }));
        return Write("retry-new", async (db, now, ct) =>
        {
            if (await Replay<JobAdmissionResult>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var job = await Find(db, request.Scope, request.JobId, ct);
            JobAdmissionResult result;
            if (job is null) result = new(JobCommandStatus.NotFound, null);
            else if (job.State != (int)JobState.Terminal || job.Revision != request.ExpectedTerminalRevision) result = new(JobCommandStatus.Conflict, null);
            else
            {
                var original = Request(job);
                var retry = original with { NotBefore = null, Association = original.Association is { } association
                    ? association with { Mode = JobAssociationMode.Independent, Continuation = null } : null };
                result = await Admit(db, new(request.Scope, request.ProposedNewJobId, request.IdempotencyKey, JobRequestDigest.Compute(retry), retry, null), now, ct, job.JobId);
            }
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.RetryAsNewRun, now, new(JobId: request.JobId, ExpectedRevision: request.ExpectedTerminalRevision)); return result;
        }, cancellationToken);
    }
}
