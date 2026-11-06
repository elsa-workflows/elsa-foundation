using System.Text.Json;
using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    public async ValueTask<JobClaimResult> ClaimAsync(JobClaimRequest request, CancellationToken cancellationToken = default)
    {
        _ = JobStoreCodec.Scope(request.Scope); JobStoreCodec.Name(request.Owner); JobStoreCodec.Name(request.RequestId!); JobStoreCodec.LeaseDuration(request.LeaseDuration);
        var queues = request.Queues.Distinct(StringComparer.Ordinal).ToArray();
        var handlers = request.Handlers.ToArray();
        if (queues.Length is < 1 or > 1000 || handlers.Length is < 1 or > 1000) throw new ArgumentException("Jobs claims need bounded nonempty queue and handler support sets.");
        foreach (var queue in queues) JobStoreCodec.Name(queue);
        foreach (var handler in handlers) { JobStoreCodec.Name(handler.ImplementationIdentity); JobStoreCodec.Name(handler.Handler.JobType); JobStoreCodec.Name(handler.Handler.Version); JobStoreCodec.Schema(handler.InputSchema); JobStoreCodec.Schema(handler.ResultSchema); }
        var supports = handlers.Select(JobStoreCodec.Support).Distinct().ToArray();
        var pins = handlers.Select(h => JobStoreCodec.Pin(JobStoreCodec.Support(h), h.ImplementationIdentity)).Distinct().ToArray();
        var requestDigest = JobStoreCodec.Hash(JobStoreCodec.Json(new { request.Scope, request.Owner, Queues = queues, Handlers = handlers, request.LeaseDuration, request.RequestId }));
        try
        {
            return await Write("claim", async (db, now, ct) =>
            {
                var scopeKey = JobStoreCodec.Scope(request.Scope);
                var grantKey = JobStoreCodec.Key(request.Scope, "claim-grant", request.Owner, request.RequestId);
                var priorGrant = await db.ClaimGrants.SingleOrDefaultAsync(x => x.Key == grantKey, ct);
                if (priorGrant is not null)
                {
                    JobStoreCodec.Exact(priorGrant, request.Scope, "claim-grant", request.Owner, request.RequestId);
                    if (priorGrant.RequestDigest != requestDigest) return new JobClaimResult(JobCommandStatus.Conflict, null);
                    // This acknowledges only the original permission. The worker must renew live authority before entry.
                    return new JobClaimResult(JobCommandStatus.Replayed, JobStoreCodec.ReadRetained<JobAttemptLease>(priorGrant.LeaseJson));
                }
                var cursorKey = JobStoreCodec.Key(request.Scope, "claim-cursor", request.Owner);
                var cursor = await db.ClaimCursors.SingleOrDefaultAsync(x => x.Key == cursorKey, ct);
                if (cursor is null) { cursor = JobStoreCodec.New<JobClaimCursorRecord>(request.Scope, "claim-cursor", request.Owner); db.ClaimCursors.Add(cursor); }
                else JobStoreCodec.Exact(cursor, request.Scope, "claim-cursor", request.Owner);
                var query = db.Jobs.Where(job => job.ScopeKey == scopeKey && job.SchemaVersion == JobsEfModule.Version && job.UnavailableDiagnosticJson == null && job.State != (int)JobState.Terminal && job.EligibleAt <= now
                    && queues.Contains(job.Queue)
                    && (job.ActiveAttemptKey == null || !db.Attempts.Any(a => a.Key == job.ActiveAttemptKey && a.ExpiresAt > now))
                    && (job.CancellationRequested || supports.Contains(job.HandlerKey) && (job.PinnedSupportKey == null || pins.Contains(job.PinnedSupportKey))
                        && !db.Memberships.Any(m => m.JobKey == job.Key && db.Gates.Any(g => g.Key == m.GateKey
                            && (g.ActiveHolds > 0 || g.Capacity != null && db.Permits.Count(p => p.GateKey == g.Key && p.ExpiresAt > now) >= g.Capacity)))));
                if (cursor.AfterKey is { } after) query = query.Where(x => string.Compare(x.Key, after) > 0);
                // Pause, capacity and compatibility are filtered BEFORE selection; a thousand held jobs cannot hide the next job.
                var candidates = await query.OrderBy(x => x.Key).Take(settings.MaximumClaimScan).ToArrayAsync(ct);
                foreach (var job in candidates)
                {
                    cursor.AfterKey = job.Key;
                    JobStoreCodec.Exact(job, request.Scope, "job", job.JobId);
                    JobRequest content;
                    try { content = Request(job); }
                    catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
                    {
                        // Preserve the original bytes and accepted identity. Commit a safe unavailable projection and
                        // advance discovery rather than rolling the cursor back onto the same malformed candidate.
                        job.UnavailableDiagnosticJson = JobStoreCodec.Json(new JobDiagnostic("jobs.request.unavailable", "The retained request cannot be read; its original content remains protected."));
                        continue;
                    }
                    var previous = job.ActiveAttemptKey is { } oldKey ? await db.Attempts.SingleOrDefaultAsync(x => x.Key == oldKey, ct) : null;
                    if (previous is not null && previous.ExpiresAt > now) continue;
                    var gates = await Gates(db, request.Scope, JobStoreCodec.Gates(content), ct);
                    if (job.CancellationRequested)
                    {
                        if (previous is not null) { previous.State = (int)JobAttemptState.OwnershipLost; await ReleasePermits(db, previous, ct); }
                        await Settle(db, request.Scope, job, previous, JobOutcome.Cancelled, null, new("jobs.cancelled", "The cancelled attempt no longer has live authority."), now, ct);
                        Touch(gates); continue;
                    }
                    if (await Availability(db, gates, now, permits: true, ct) is not null) continue;
                    var support = handlers.FirstOrDefault(h => JobStoreCodec.Support(h) == job.HandlerKey
                        && (job.ImplementationIdentity is null || h.ImplementationIdentity == job.ImplementationIdentity));
                    if (support is null) continue;
                    Touch(gates);
                    if (previous is not null)
                    {
                        previous.State = (int)JobAttemptState.OwnershipLost;
                        previous.DiagnosticJson = JobStoreCodec.Json(new JobDiagnostic("jobs.lease-lost", "The previous attempt's authority expired; physical work may still exist."));
                        await ReleasePermits(db, previous, ct);
                    }
                    if (job.AttemptCount >= content.Policy.Retry.MaximumAttempts)
                    {
                        await Settle(db, request.Scope, job, previous, previous is not null && previous.Deadline <= now ? JobOutcome.TimedOut : JobOutcome.Failed, null,
                            new("jobs.attempt-budget-exhausted", "The accepted attempt budget is exhausted after ownership recovery."), now, ct);
                        continue;
                    }
                    var attemptId = Guid.NewGuid().ToString("N");
                    var attempt = JobStoreCodec.New<JobAttemptRecord>(request.Scope, "attempt", job.JobId, attemptId);
                    attempt.JobKey = job.Key; attempt.AttemptId = attemptId; attempt.Number = checked(job.AttemptCount + 1);
                    attempt.Owner = request.Owner; attempt.Fence = checked(job.Fence + 1); attempt.OwnershipRevision = 1;
                    attempt.GrantedAt = now; attempt.ExpiresAt = checked(now + request.LeaseDuration.Ticks);
                    attempt.Deadline = checked(now + content.Policy.AttemptTimeout.Ticks); attempt.State = (int)JobAttemptState.Permitted;
                    attempt.ImplementationIdentity = support.ImplementationIdentity;
                    db.Attempts.Add(attempt);
                    job.ActiveAttemptKey = attempt.Key; job.AttemptCount = attempt.Number; job.Fence = attempt.Fence;
                    job.ImplementationIdentity ??= support.ImplementationIdentity;
                    job.PinnedSupportKey ??= JobStoreCodec.Pin(job.HandlerKey, support.ImplementationIdentity);
                    job.State = (int)JobState.AttemptActive; job.Revision = checked(job.Revision + 1); job.ProgressJson = null;
                    foreach (var gate in gates)
                    {
                        var permit = JobStoreCodec.New<JobPermitRecord>(request.Scope, "permit", attempt.AttemptId, gate.Key);
                        permit.AttemptKey = attempt.Key; permit.GateKey = gate.Key; permit.ExpiresAt = attempt.ExpiresAt; db.Permits.Add(permit);
                    }
                    AuthorizeUntil(db, attempt.ExpiresAt);
                    var lease = Lease(request.Scope, job, attempt);
                    var grant = JobStoreCodec.New<JobClaimGrantRecord>(request.Scope, "claim-grant", request.Owner, request.RequestId);
                    grant.RequestDigest = requestDigest; grant.LeaseJson = JobStoreCodec.Json(lease); db.ClaimGrants.Add(grant);
                    return new JobClaimResult(JobCommandStatus.Applied, lease);
                }
                if (candidates.Length < settings.MaximumClaimScan) cursor.AfterKey = null;
                return new JobClaimResult(JobCommandStatus.NoEligibleWork, null);
            }, cancellationToken);
        }
        catch (LeaseExpiredDuringWriteException) { return new(JobCommandStatus.Unavailable, null); }
    }

    public async ValueTask<JobRenewal> RenewAsync(JobAttemptLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.LeaseDuration(leaseDuration);
        var requestDigest = JobStoreCodec.Hash(JobStoreCodec.Json(new { Lease = lease, Duration = leaseDuration }));
        try
        {
            return await Write("renew", async (db, now, ct) =>
            {
                var job = await Find(db, lease.Scope, lease.JobId, ct);
                var attempt = job is null ? null : await Attempt(db, lease.Scope, job, lease.AttemptId, ct);
                if (job is not null && attempt is not null && attempt.LastRenewalDigest == requestDigest && attempt.LastRenewalLeaseJson is { } prior)
                {
                    var priorLease = JobStoreCodec.ReadRetained<JobAttemptLease>(prior);
                    if (Owns(job, attempt, priorLease, now))
                    {
                        AuthorizeUntil(db, attempt.ExpiresAt);
                        return new JobRenewal(JobCommandStatus.Replayed, priorLease, job.CancellationRequested, attempt.Deadline <= now);
                    }
                    return new JobRenewal(JobCommandStatus.LeaseLost, null, job.CancellationRequested, attempt.Deadline <= now);
                }
                if (job is null || attempt is null || !Owns(job, attempt, lease, now)) return new JobRenewal(JobCommandStatus.LeaseLost, null, job?.CancellationRequested ?? false, attempt is not null && attempt.Deadline <= now);
                AuthorizeUntil(db, attempt.ExpiresAt);
                // Renewal participates in scope arbitration: a new start cannot count a stale permit expiry.
                Touch(await Gates(db, lease.Scope, JobStoreCodec.Gates(Request(job)), ct));
                attempt.ExpiresAt = Math.Max(attempt.ExpiresAt, checked(now + leaseDuration.Ticks));
                attempt.OwnershipRevision = checked(attempt.OwnershipRevision + 1);
                foreach (var permit in await db.Permits.Where(x => x.AttemptKey == attempt.Key).ToArrayAsync(ct)) permit.ExpiresAt = attempt.ExpiresAt;
                var renewedLease = Lease(lease.Scope, job, attempt);
                attempt.LastRenewalDigest = requestDigest; attempt.LastRenewalLeaseJson = JobStoreCodec.Json(renewedLease);
                return new JobRenewal(JobCommandStatus.Applied, renewedLease, job.CancellationRequested, attempt.Deadline <= now);
            }, cancellationToken);
        }
        catch (LeaseExpiredDuringWriteException) { return new(JobCommandStatus.LeaseLost, null, false, false); }
    }

    public async ValueTask<JobTransition> RecordPhysicalStartAsync(JobAttemptLease lease, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Write("physical-start", async (db, now, ct) =>
            {
                var job = await Find(db, lease.Scope, lease.JobId, ct);
                var attempt = job is null ? null : await Attempt(db, lease.Scope, job, lease.AttemptId, ct);
                if (job is null || attempt is null || !Owns(job, attempt, lease, now)) return Transition(JobCommandStatus.LeaseLost, job);
                AuthorizeUntil(db, attempt.ExpiresAt);
                if (attempt.PhysicalStartedAt is not null) return Transition(JobCommandStatus.Replayed, job);
                attempt.PhysicalStartedAt = now; attempt.State = (int)JobAttemptState.Running;
                return Transition(JobCommandStatus.Applied, job);
            }, cancellationToken);
        }
        catch (LeaseExpiredDuringWriteException) { return Transition(JobCommandStatus.LeaseLost); }
    }

    public async ValueTask<JobTransition> RecordProgressAsync(JobAttemptLease lease, JobProgress progress, CancellationToken cancellationToken = default)
    {
        var json = JobStoreCodec.Preflight(() =>
        {
            if (progress.Data is { } data) JobStoreCodec.Payload(data);
            if (progress.Fraction is { } fraction && (!double.IsFinite(fraction) || fraction is < 0 or > 1) || progress.Message?.Length > 2048
                || JsonSerializer.SerializeToUtf8Bytes(progress).Length > JobPayloadLimits.MaximumProgressBytes)
                throw new ArgumentException("The Jobs progress record is invalid or exceeds its bound.");
            return JobStoreCodec.Json(progress);
        }, "jobs.progress.invalid");
        try
        {
            return await Write("progress", async (db, now, ct) =>
            {
                var job = await Find(db, lease.Scope, lease.JobId, ct);
                var attempt = job is null ? null : await Attempt(db, lease.Scope, job, lease.AttemptId, ct);
                if (job is null || attempt is null || !Owns(job, attempt, lease, now)) return Transition(JobCommandStatus.LeaseLost, job);
                AuthorizeUntil(db, attempt.ExpiresAt); job.ProgressJson = json;
                // Job content CAS fences a concurrent completion; ownership revision is unchanged.
                return Transition(JobCommandStatus.Applied, job);
            }, cancellationToken);
        }
        catch (LeaseExpiredDuringWriteException) { return Transition(JobCommandStatus.LeaseLost); }
    }

    public async ValueTask<JobTransition> CompleteAsync(JobAttemptLease lease, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default)
    {
        var frozen = JobStoreCodec.Preflight(() =>
        {
            JobStoreCodec.Receipt(receipt);
            if (receipt.AttemptId != lease.AttemptId || receipt.Owner != lease.Owner || receipt.Fence != lease.Fence) throw new ArgumentException("The receipt does not identify the presented attempt.");
            return JobStoreCodec.Read<JobPhysicalReceipt>(JobStoreCodec.Json(receipt));
        }, "jobs.payload.invalid");
        try
        {
            return await Write("complete", async (db, now, ct) =>
            {
                var job = await Find(db, lease.Scope, lease.JobId, ct);
                var attempt = job is null ? null : await Attempt(db, lease.Scope, job, receipt.AttemptId, ct);
                if (job is null || attempt is null) return Transition(JobCommandStatus.NotFound);
                var record = await Receipt(db, lease.Scope, job, attempt, frozen, now, ct);
                if (record is null) return Transition(JobCommandStatus.Conflict, job);
                if (attempt.CompletionReceiptKey == record.Key) return Transition(JobCommandStatus.Replayed, job);
                if (!Owns(job, attempt, lease, now)) return Transition(JobCommandStatus.LeaseLost, job);
                AuthorizeUntil(db, attempt.ExpiresAt);
                var content = Request(job);
                if (receipt.Payload is { } payload && payload.Schema != content.ResultSchema) throw new ArgumentException("The handler result schema differs from the accepted pin.");
                attempt.CompletionReceiptKey = record.Key; attempt.PhysicalExitedAt = now;
                attempt.DiagnosticJson = receipt.Diagnostic is null ? null : JobStoreCodec.Json(receipt.Diagnostic);
                var outcome = job.CancellationRequested ? JobAttemptOutcome.Cancelled : attempt.Deadline <= now ? JobAttemptOutcome.TimedOut : receipt.Outcome;
                attempt.State = (int)(outcome switch
                {
                    JobAttemptOutcome.Succeeded => JobAttemptState.Succeeded,
                    JobAttemptOutcome.TimedOut => JobAttemptState.TimedOut,
                    JobAttemptOutcome.Cancelled => JobAttemptState.Cancelled,
                    _ => JobAttemptState.Failed
                });
                var gates = await Gates(db, lease.Scope, JobStoreCodec.Gates(content), ct); Touch(gates);
                await ReleasePermits(db, attempt, ct); job.ActiveAttemptKey = null; job.Revision = checked(job.Revision + 1);
                var retry = !job.CancellationRequested && attempt.Number < content.Policy.Retry.MaximumAttempts
                    && (outcome == JobAttemptOutcome.Failed && receipt.Retryable || outcome == JobAttemptOutcome.TimedOut && content.Policy.Retry.RetryTimeouts || outcome == JobAttemptOutcome.Abandoned);
                if (retry)
                {
                    var policy = content.Policy.Retry;
                    // Zero remains zero even when a valid multiplier/exponent overflows Math.Pow.
                    // Saturate BEFORE multiplication so Infinity * 0 can never become a NaN delay.
                    var initial = policy.InitialBackoff.Ticks;
                    var power = initial == 0 ? 1 : Math.Pow(policy.BackoffMultiplier, attempt.Number - 1);
                    var ticks = initial == 0 ? 0 : power >= policy.MaximumBackoff.Ticks / (double)initial
                        ? policy.MaximumBackoff.Ticks : initial * power;
                    var jitter = (Convert.ToUInt32(JobStoreCodec.Hash(job.Key + ":" + attempt.Number)[..8], 16) / (double)uint.MaxValue * 2 - 1) * policy.JitterFraction;
                    job.EligibleAt = checked(now + (long)Math.Clamp(ticks * (1 + jitter), 0, policy.MaximumBackoff.Ticks));
                    job.State = (int)JobState.Accepted;
                }
                else await Settle(db, lease.Scope, job, attempt, outcome switch
                    {
                        JobAttemptOutcome.Succeeded => JobOutcome.Succeeded,
                        JobAttemptOutcome.Cancelled => JobOutcome.Cancelled,
                        JobAttemptOutcome.TimedOut => JobOutcome.TimedOut,
                        _ => JobOutcome.Failed
                    }, outcome == JobAttemptOutcome.Succeeded ? receipt.Payload : null, receipt.Diagnostic, now, ct);
                return Transition(JobCommandStatus.Applied, job);
            }, cancellationToken);
        }
        catch (LeaseExpiredDuringWriteException)
        {
            await RecordPhysicalReceiptAsync(lease.Scope, lease.JobId, frozen, cancellationToken);
            return Transition(JobCommandStatus.LeaseLost);
        }
    }

    public ValueTask<JobTransition> RecordPhysicalReceiptAsync(JobScope scope, string jobId, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default)
    {
        var frozen = JobStoreCodec.Preflight(() =>
        {
            JobStoreCodec.Receipt(receipt);
            return JobStoreCodec.Read<JobPhysicalReceipt>(JobStoreCodec.Json(receipt));
        }, "jobs.payload.invalid");
        return Write("physical-receipt", async (db, now, ct) =>
        {
            var job = await Find(db, scope, jobId, ct);
            var attempt = job is null ? null : await Attempt(db, scope, job, frozen.AttemptId, ct);
            if (job is null || attempt is null) return Transition(JobCommandStatus.NotFound);
            var record = await Receipt(db, scope, job, attempt, frozen, now, ct);
            if (record is null) return Transition(JobCommandStatus.Conflict, job);
            // A late report is evidence only: no lease, retry, permit or authoritative result mutation.
            return Transition(db.Entry(record).State == EntityState.Added ? JobCommandStatus.Applied : JobCommandStatus.Replayed, job);
        }, cancellationToken);
    }

    private static async ValueTask<JobPhysicalReceiptRecord?> Receipt(JobsDbContext db, JobScope scope, JobRecord job, JobAttemptRecord attempt, JobPhysicalReceipt receipt, long now, CancellationToken ct)
    {
        if (attempt.Owner != receipt.Owner || attempt.Fence != receipt.Fence) return null;
        if (job.ResultExpiredAt is not null)
        {
            var metadata = RequestMetadata(job); _ = Retention(job, ResultMetadata(job, metadata));
        }
        var identity = JobStoreCodec.Key(scope, "receipt", receipt.ReceiptId);
        var json = JobStoreCodec.Json(receipt); var hash = JobStoreCodec.Hash(json);
        var existing = await db.Receipts.SingleOrDefaultAsync(x => x.Key == identity, ct);
        if (existing is not null)
        {
            JobStoreCodec.Exact(existing, scope, "receipt", receipt.ReceiptId);
            _ = ReceiptInspection(scope, job, existing);
            return existing.JobKey == job.Key && existing.AttemptKey == attempt.Key && existing.ReceiptSha256 == hash &&
                (existing.ReceiptJson == json || existing.PayloadExpiredAt is not null) ? existing : null;
        }
        var row = JobStoreCodec.New<JobPhysicalReceiptRecord>(scope, "receipt", receipt.ReceiptId);
        row.JobKey = job.Key; row.AttemptKey = attempt.Key; row.ReceiptJson = json; row.ReceiptSha256 = hash; db.Receipts.Add(row);
        // Serialize payload insertion with retention using the job's row token, without changing its terminal revision.
        db.Entry(job).Property(value => value.Token).IsModified = true;
        SaveReceiptMetadata(row, new(receipt.ReceiptId, receipt.AttemptId, receipt.Owner, receipt.Fence, receipt.Outcome,
            receipt.Payload?.Schema, receipt.PayloadSha256, receipt.Diagnostic, receipt.Retryable, receipt.ReportedAt, hash));
        if (job.ResultExpiredAt is { } expired)
        {
            var metadata = RequestMetadata(job); _ = Retention(job, ResultMetadata(job, metadata));
            row.ReceiptJson = null; row.PayloadExpiredAt = Math.Max(now, expired);
        }
        // Retained late exit is observational; it never restores authority or changes the selected outcome.
        attempt.PhysicalExitedAt ??= now;
        return row;
    }

    private static async ValueTask Settle(JobsDbContext db, JobScope scope, JobRecord job, JobAttemptRecord? attempt,
        JobOutcome outcome, JobPayload? payload, JobDiagnostic? diagnostic, long now, CancellationToken ct)
    {
        if (job.ResultJson is not null || job.ResultMetadataJson is not null) throw new InvalidOperationException("An authoritative Jobs result is immutable.");
        job.State = (int)JobState.Terminal; job.ActiveAttemptKey = null; job.Revision = checked(job.Revision + 1);
        var resultId = JobStoreCodec.Hash(JobStoreCodec.Identity("jobs.result.v1", job.Key, attempt?.AttemptId, job.Revision));
        var result = new JobResult(resultId, job.JobId, attempt?.AttemptId, outcome, payload, diagnostic, JobPayloadDigest.Compute(payload), JobStoreCodec.At(now));
        job.ResultJson = JobStoreCodec.Json(result);
        job.ResultMetadataJson = JobStoreCodec.Json(JobResultMetadata.From(result));
        job.ResultMetadataSha256 = JobStoreCodec.Hash(job.ResultMetadataJson);
        job.InputExpiresAt = job.TerminalInputRetentionTicks is { } inputRetention ? Expiry(now, inputRetention) : null;
        job.ResultExpiresAt = job.TerminalResultRetentionTicks is { } resultRetention ? Expiry(now, resultRetention) : null;
        var association = Request(job).Association;
        if (association is { Mode: JobAssociationMode.Awaited, Continuation: not null })
        {
            var delivery = JobStoreCodec.New<JobDeliveryRecord>(scope, "delivery", job.JobId);
            delivery.JobKey = job.Key; delivery.ResultId = resultId; delivery.AssociationJson = JobStoreCodec.Json(association);
            db.Deliveries.Add(delivery);
        }
        await ValueTask.CompletedTask;
    }
}
