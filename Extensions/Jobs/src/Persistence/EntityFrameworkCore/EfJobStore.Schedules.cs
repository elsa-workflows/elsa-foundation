using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    private DateTimeOffset? Next(JobRecurrence expression, DateTimeOffset after)
    {
        if (recurrence is null) throw new JobException("jobs.recurrence.unavailable", "The host has not installed a Jobs recurrence calculator.");
        var next = recurrence.Next(expression, after);
        if (next <= after) throw new InvalidOperationException("The Jobs recurrence calculator did not advance its UTC cursor.");
        return next?.ToUniversalTime();
    }

    public ValueTask<JobTransition> SaveScheduleAsync(JobSaveScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var frozen = JobStoreCodec.Preflight(() =>
        {
            JobStoreCodec.Name(request.Definition.ScheduleId); JobStoreCodec.Request(request.Definition.Template); JobStoreCodec.Operation(request.Operation);
            if (!Enum.IsDefined(request.Definition.MissedPolicy) || !Enum.IsDefined(request.Definition.Recurrence.Kind)
                || request.Definition.Recurrence.Expression.Length > 1024 || request.ExpectedRevision < 0)
                throw new ArgumentException("The Jobs schedule policy or recurrence is invalid.");
            if (request.Definition.Template.Association is { Mode: JobAssociationMode.Awaited }) throw new ArgumentException("A recurring schedule cannot reuse one awaited model address.");
            _ = Next(request.Definition.Recurrence, request.Definition.FirstOccurrence);
            return JobStoreCodec.Read<JobSaveScheduleRequest>(JobStoreCodec.Json(request));
        }, "jobs.request.invalid");
        var digest = JobStoreCodec.Preflight(() => JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "save-schedule", request = frozen })), "jobs.request.invalid");
        return Write("save-schedule", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, frozen.Scope, frozen.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var key = JobStoreCodec.Key(frozen.Scope, "schedule", frozen.Definition.ScheduleId);
            var row = await db.Schedules.SingleOrDefaultAsync(x => x.Key == key, ct);
            JobTransition result;
            var definitionJson = JobStoreCodec.Json(frozen.Definition);
            if (row is not null) JobStoreCodec.Exact(row, frozen.Scope, "schedule", frozen.Definition.ScheduleId);
            if ((row?.Revision ?? 0) != frozen.ExpectedRevision) result = new(JobCommandStatus.Conflict, row?.Revision, null, null);
            else if (row is not null && row.NextOccurrence <= now && row.DefinitionJson != definitionJson)
                // Preserve owed cursor responsibility; replacement must wait for its explicit materialization/accounting.
                result = new(JobCommandStatus.Conflict, row.Revision, null, null);
            else
            {
                if (row is null)
                {
                    row = JobStoreCodec.New<JobScheduleRecord>(frozen.Scope, "schedule", frozen.Definition.ScheduleId);
                    row.CreatedAt = now; row.NextOccurrence = frozen.Definition.FirstOccurrence.UtcTicks;
                    row.Enabled = true; row.DefinitionRevision = 1; db.Schedules.Add(row);
                }
                else if (row.DefinitionJson != definitionJson)
                {
                    row.DefinitionRevision = checked(row.DefinitionRevision + 1);
                    row.NextOccurrence = frozen.Definition.FirstOccurrence.UtcTicks;
                    row.CoalesceNext = null; row.CoalesceCutoff = null; row.CoalesceCount = 0; row.CoalesceThrough = null;
                    db.Memberships.RemoveRange(await db.Memberships.Where(x => x.JobKey == row.Key).ToArrayAsync(ct));
                }
                row.DefinitionJson = definitionJson; row.Revision = checked(row.Revision + 1); row.UpdatedAt = now;
                foreach (var gate in await Gates(db, frozen.Scope, JobStoreCodec.Gates(frozen.Definition.Template), ct))
                {
                    var membershipKey = JobStoreCodec.Key(frozen.Scope, "membership", row.Key, gate.Key);
                    var existingMembership = db.Memberships.Local.SingleOrDefault(x => x.Key == membershipKey)
                        ?? await db.Memberships.SingleOrDefaultAsync(x => x.Key == membershipKey, ct);
                    if (existingMembership is not null && db.Entry(existingMembership).State == EntityState.Deleted)
                        db.Entry(existingMembership).State = EntityState.Unchanged;
                    else if (existingMembership is null)
                    {
                        var membership = JobStoreCodec.New<JobGateMembershipRecord>(frozen.Scope, "membership", row.Key, gate.Key);
                        membership.JobKey = row.Key; membership.GateKey = gate.Key; db.Memberships.Add(membership);
                    }
                }
                result = new(JobCommandStatus.Applied, row.Revision, null, null);
            }
            Remember(db, frozen.Scope, frozen.Operation, digest, result); return result;
        }, cancellationToken);
    }

    public ValueTask<JobTransition> SetScheduleEnabledAsync(JobSetScheduleEnabledRequest request, CancellationToken cancellationToken = default)
    {
        JobStoreCodec.Name(request.ScheduleId); JobStoreCodec.Operation(request.Operation);
        var digest = JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "schedule-enabled", request }));
        return Write("schedule-enabled", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, request.Scope, request.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            var key = JobStoreCodec.Key(request.Scope, "schedule", request.ScheduleId);
            var row = await db.Schedules.SingleOrDefaultAsync(x => x.Key == key, ct);
            JobTransition result;
            if (row is null) result = new(JobCommandStatus.NotFound, null, null, null);
            else
            {
                JobStoreCodec.Exact(row, request.Scope, "schedule", request.ScheduleId);
                if (row.Revision != request.ExpectedRevision) result = new(JobCommandStatus.Conflict, row.Revision, null, null);
                else { row.Enabled = request.Enabled; row.Revision = checked(row.Revision + 1); row.UpdatedAt = now; result = new(JobCommandStatus.Applied, row.Revision, null, null); }
            }
            Remember(db, request.Scope, request.Operation, digest, result); return result;
        }, cancellationToken);
    }

    public ValueTask<JobMaterializationResult> MaterializeDueAsync(JobMaterializationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.MaximumOccurrences is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(request));
        var scopeKey = JobStoreCodec.Scope(request.Scope);
        return Write("materialize", async (db, now, ct) =>
        {
            var cursorKey = JobStoreCodec.Key(request.Scope, "schedule-cursor");
            var cursor = await db.ClaimCursors.SingleOrDefaultAsync(x => x.Key == cursorKey, ct);
            if (cursor is null) { cursor = JobStoreCodec.New<JobClaimCursorRecord>(request.Scope, "schedule-cursor"); db.ClaimCursors.Add(cursor); }
            else JobStoreCodec.Exact(cursor, request.Scope, "schedule-cursor");
            var query = db.Schedules.Where(s => s.ScopeKey == scopeKey && s.Enabled && s.NextOccurrence <= now
                && !db.Memberships.Any(m => m.JobKey == s.Key && db.Gates.Any(g => g.Key == m.GateKey && g.ActiveHolds > 0)));
            if (cursor.AfterKey is { } after) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var schedules = await query.OrderBy(x => x.Key).Take(request.MaximumOccurrences).ToArrayAsync(ct);
            var receipts = new List<JobOccurrenceReceipt>();
            var madeProgress = false;
            foreach (var schedule in schedules)
            {
                cursor.AfterKey = schedule.Key;
                var definition = JobStoreCodec.ReadRetained<JobScheduleDefinition>(schedule.DefinitionJson);
                JobStoreCodec.Exact(schedule, request.Scope, "schedule", definition.ScheduleId);
                var gates = await Gates(db, request.Scope, JobStoreCodec.Gates(definition.Template), ct);
                if (await Availability(db, gates, now, permits: false, ct) is not null) continue;
                Touch(gates);
                if (definition.MissedPolicy == JobMissedOccurrencePolicy.CoalesceOne)
                {
                    // Freeze one finite owed interval. Later wall time is responsibility for the NEXT
                    // cycle, not a moving target that can starve this catch-up forever.
                    schedule.CoalesceCutoff ??= now;
                    var cutoff = schedule.CoalesceCutoff.Value;
                    var next = schedule.CoalesceNext is { } c ? JobStoreCodec.At(c) : Next(definition.Recurrence, JobStoreCodec.At(schedule.NextOccurrence));
                    var arithmetic = 0;
                    // Count an arbitrarily long outage in durable finite slices; no silent replay cap or accepted-job deletion.
                    while (next is { } dueNext && dueNext.UtcTicks <= cutoff && arithmetic++ < settings.MaximumClaimScan)
                    {
                        schedule.CoalesceCount = checked(schedule.CoalesceCount + 1); schedule.CoalesceThrough = next.Value.UtcTicks;
                        next = Next(definition.Recurrence, next.Value);
                    }
                    schedule.CoalesceNext = next?.UtcTicks;
                    if (next is { } remaining && remaining.UtcTicks <= cutoff)
                    {
                        schedule.Revision = checked(schedule.Revision + 1); schedule.UpdatedAt = now; madeProgress = true;
                        continue;
                    }
                    var receipt = await Materialize(db, request.Scope, schedule, definition, schedule.NextOccurrence,
                        schedule.CoalesceCount, schedule.CoalesceThrough, now, ct);
                    receipts.Add(receipt);
                    schedule.CoalesceNext = null; schedule.CoalesceCutoff = null; schedule.CoalesceCount = 0; schedule.CoalesceThrough = null;
                    Advance(schedule, next, now); madeProgress = true;
                }
                else
                {
                    while (schedule.Enabled && schedule.NextOccurrence <= now && receipts.Count < request.MaximumOccurrences)
                    {
                        var due = schedule.NextOccurrence;
                        receipts.Add(await Materialize(db, request.Scope, schedule, definition, due, 0, null, now, ct));
                        Advance(schedule, Next(definition.Recurrence, JobStoreCodec.At(due)), now); madeProgress = true;
                    }
                }
                if (receipts.Count >= request.MaximumOccurrences) break;
            }
            if (schedules.Length < request.MaximumOccurrences) cursor.AfterKey = null;
            return new JobMaterializationResult(madeProgress ? JobCommandStatus.Applied : JobCommandStatus.NoEligibleWork, receipts);
        }, cancellationToken);
    }

    private async ValueTask<JobOccurrenceReceipt> Materialize(JobsDbContext db, JobScope scope, JobScheduleRecord schedule,
        JobScheduleDefinition definition, long due, long count, long? through, long now, CancellationToken ct)
    {
        var occurrenceId = JobStoreCodec.Hash(JobStoreCodec.Identity("jobs.occurrence.v1", definition.ScheduleId, schedule.DefinitionRevision, due));
        var key = JobStoreCodec.Key(scope, "occurrence", occurrenceId);
        var existing = await db.Occurrences.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (existing is not null)
        {
            JobStoreCodec.Exact(existing, scope, "occurrence", occurrenceId);
            return JobStoreCodec.ReadRetained<JobOccurrenceReceipt>(existing.ReceiptJson);
        }
        var jobRequest = definition.Template with { NotBefore = JobStoreCodec.At(due) };
        var digest = JobRequestDigest.Compute(jobRequest);
        var jobId = JobStoreCodec.Hash(JobStoreCodec.Address(scope, "schedule-job", occurrenceId));
        var admission = new JobAdmission(scope, jobId, occurrenceId, digest, jobRequest,
            new("schedule", definition.ScheduleId, occurrenceId, digest));
        var accepted = await Admit(db, admission, now, ct);
        if (accepted.Acceptance is null) throw new InvalidDataException("A Jobs schedule occurrence could not resolve its protected admission.");
        var receipt = new JobOccurrenceReceipt(definition.ScheduleId, schedule.DefinitionRevision, JobStoreCodec.At(due), accepted.Acceptance.JobId, count,
            through is { } t ? JobStoreCodec.At(t) : null);
        var row = JobStoreCodec.New<JobOccurrenceRecord>(scope, "occurrence", occurrenceId);
        row.ReceiptJson = JobStoreCodec.Json(receipt); db.Occurrences.Add(row);
        return receipt;
    }

    private static void Advance(JobScheduleRecord schedule, DateTimeOffset? next, long now)
    {
        schedule.NextOccurrence = next?.UtcTicks ?? DateTimeOffset.MaxValue.UtcTicks;
        if (next is null) schedule.Enabled = false;
        schedule.Revision = checked(schedule.Revision + 1); schedule.UpdatedAt = now;
    }
}
