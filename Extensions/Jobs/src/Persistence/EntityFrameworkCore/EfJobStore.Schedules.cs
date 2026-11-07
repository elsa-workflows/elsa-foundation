using System.Text.Json;
using Elsa.Jobs.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    private DateTimeOffset? Next(JobRecurrence expression, DateTimeOffset after)
    {
        if (recurrence is null) throw new JobException("jobs.recurrence.unavailable", "The host has not installed a Jobs recurrence calculator.");
        try
        {
            var next = recurrence.Next(expression, after);
            if (next <= after) throw new JobException("jobs.recurrence.unavailable", "The Jobs recurrence calculator could not advance its UTC cursor.");
            return next?.ToUniversalTime();
        }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        { throw new JobException("jobs.recurrence.invalid", "The Jobs UTC recurrence expression is invalid or unsupported.", error); }
        catch (NotSupportedException error)
        { throw new JobException("jobs.recurrence.unavailable", "The host cannot currently calculate this Jobs recurrence.", error); }
    }

    private static JobDiagnostic ScheduleUnavailable() => new("jobs.schedule.unavailable", "The retained schedule definition is unavailable; its original bytes and owed cursor remain protected.");
    private static JobDiagnostic RecurrenceUnavailable() => new("jobs.recurrence.unavailable", "The host cannot currently calculate this schedule; its owed cursor remains available for retry.");
    private static bool InvalidSchedule(Exception error) => error is InvalidDataException or JsonException or NotSupportedException or ArgumentException or FormatException or OverflowException
        || JobStoreCodec.SerializationFailure(error) || error is JobException { Code: "jobs.recurrence.invalid" };

    private static string ScheduleId(JobScope scope, JobScheduleRecord row)
    {
        try
        {
            // Legacy identities already contain the ID. Check every kind/length before indexing;
            // a corrupted definition must never supply the identity used for its metadata projection.
            if (row.Identity.Length > 8192) throw new InvalidDataException("The retained Jobs schedule identity exceeds its bound.");
            using var json = JsonDocument.Parse(row.Identity, new JsonDocumentOptions { MaxDepth = 8 });
            var frame = json.RootElement;
            if (frame.ValueKind != JsonValueKind.Array || frame.GetArrayLength() != 4
                || frame[0].ValueKind != JsonValueKind.String || frame[1].ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || frame[2].ValueKind != JsonValueKind.String || frame[3].ValueKind != JsonValueKind.Array || frame[3].GetArrayLength() != 1
                || frame[3][0].ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The retained Jobs schedule identity has an invalid frame.");
            JobStoreCodec.Name(frame[0].GetString()!);
            if (frame[1].ValueKind == JsonValueKind.String) JobStoreCodec.Name(frame[1].GetString()!);
            JobStoreCodec.Name(frame[2].GetString()!);
            var id = frame[3][0].GetString()!; JobStoreCodec.Name(id);
            JobStoreCodec.ExactIdentity(row, scope, "schedule", id);
            if (row.Key != JobStoreCodec.Key(scope, "schedule", id)) throw new InvalidDataException("The retained Jobs schedule identity does not match its address.");
            return id;
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { throw new InvalidDataException("The retained Jobs schedule identity is invalid.", error); }
    }

    private static void ValidateDefinition(JobScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(definition.Recurrence);
        JobStoreCodec.Name(definition.ScheduleId); JobStoreCodec.Request(definition.Template);
        if (!Enum.IsDefined(definition.MissedPolicy) || !Enum.IsDefined(definition.Recurrence.Kind)
            || string.IsNullOrWhiteSpace(definition.Recurrence.Expression) || definition.Recurrence.Expression.Length > 1024)
            throw new ArgumentException("The Jobs schedule policy or recurrence is invalid.");
        if (definition.Template.Association is { Mode: JobAssociationMode.Awaited }) throw new ArgumentException("A recurring schedule cannot reuse one awaited model address.");
    }

    private static JobScheduleDefinition Definition(JobScope scope, JobScheduleRecord row)
    {
        var id = ScheduleId(scope, row);
        var definition = JobStoreCodec.ReadRetained<JobScheduleDefinition>(row.DefinitionJson, ValidateDefinition);
        if (definition.ScheduleId != id) throw new InvalidDataException("The retained Jobs schedule definition differs from its exact identity.");
        return definition;
    }

    private sealed record PlannedOccurrence(long Due, long Count, long? Through);
    private sealed record SchedulePlan(IReadOnlyList<PlannedOccurrence> Occurrences, DateTimeOffset? Next,
        bool Counting, long? CoalesceNext = null, long? Cutoff = null, long Count = 0, long? Through = null);

    private SchedulePlan Plan(JobScheduleRecord schedule, JobScheduleDefinition definition, long now, int maximum, CancellationToken ct)
    {
        // Calculate the entire bounded slice before mutating tracked rows or accepting an occurrence.
        // Invalid arithmetic at any position cannot leave a partly accepted replay or advanced cursor.
        ct.ThrowIfCancellationRequested();
        DateTimeOffset? CalculateNext(DateTimeOffset after)
        {
            ct.ThrowIfCancellationRequested();
            var calculated = Next(definition.Recurrence, after);
            ct.ThrowIfCancellationRequested();
            return calculated;
        }
        if (definition.MissedPolicy == JobMissedOccurrencePolicy.CoalesceOne)
        {
            var cutoff = schedule.CoalesceCutoff ?? now;
            var count = schedule.CoalesceCount; var through = schedule.CoalesceThrough;
            var next = schedule.CoalesceNext is { } c ? JobStoreCodec.At(c) : CalculateNext(JobStoreCodec.At(schedule.NextOccurrence));
            var arithmetic = 0;
            while (next is { } due && due.UtcTicks <= cutoff && arithmetic++ < settings.MaximumClaimScan)
            {
                ct.ThrowIfCancellationRequested();
                count = checked(count + 1); through = due.UtcTicks;
                next = CalculateNext(due);
            }
            return next is { } remaining && remaining.UtcTicks <= cutoff
                ? new([], null, true, remaining.UtcTicks, cutoff, count, through)
                : new([new(schedule.NextOccurrence, count, through)], next, false);
        }
        var occurrences = new List<PlannedOccurrence>();
        DateTimeOffset? cursor = JobStoreCodec.At(schedule.NextOccurrence);
        while (cursor is { } due && due.UtcTicks <= now && occurrences.Count < maximum)
        {
            ct.ThrowIfCancellationRequested();
            var next = CalculateNext(due);
            occurrences.Add(new(due.UtcTicks, 0, null)); cursor = next;
        }
        return new(occurrences, cursor, false);
    }

    public ValueTask<JobTransition> SaveScheduleAsync(JobSaveScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var frozen = JobStoreCodec.Preflight(() =>
        {
            ValidateDefinition(request.Definition); JobStoreCodec.Operation(request.Operation);
            if (request.ExpectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(request));
            return JobStoreCodec.Read<JobSaveScheduleRequest>(JobStoreCodec.Json(request));
        }, "jobs.request.invalid");
        var digest = JobStoreCodec.Preflight(() => JobStoreCodec.Hash(JobStoreCodec.Json(new { kind = "save-schedule", request = frozen })), "jobs.request.invalid");
        return Write("save-schedule", async (db, now, ct) =>
        {
            if (await Replay<JobTransition>(db, frozen.Scope, frozen.Operation, digest, ct) is { } replay) return replay with { Status = replay.Status == JobCommandStatus.Applied ? JobCommandStatus.Replayed : replay.Status };
            // Recognition of a committed operation must not depend on today's calculator availability.
            ct.ThrowIfCancellationRequested();
            _ = Next(frozen.Definition.Recurrence, frozen.Definition.FirstOccurrence);
            ct.ThrowIfCancellationRequested();
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
                row.DefinitionJson = definitionJson; row.UnavailableDiagnosticJson = null; row.Revision = checked(row.Revision + 1); row.UpdatedAt = now;
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
            Remember(db, frozen.Scope, frozen.Operation, digest, result, JobOperationKind.SaveSchedule, now, new(ScheduleId: frozen.Definition.ScheduleId, ExpectedRevision: frozen.ExpectedRevision)); return result;
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
            Remember(db, request.Scope, request.Operation, digest, result, JobOperationKind.SetScheduleEnabled, now, new(ScheduleId: request.ScheduleId, ExpectedRevision: request.ExpectedRevision, Enabled: request.Enabled)); return result;
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
            var query = db.Schedules.Where(s => s.ScopeKey == scopeKey && s.SchemaVersion == JobsEfModule.Version && s.UnavailableDiagnosticJson == null && s.Enabled && s.NextOccurrence <= now
                && !db.Memberships.Any(m => m.JobKey == s.Key && db.Gates.Any(g => g.Key == m.GateKey && g.ActiveHolds > 0)));
            if (cursor.AfterKey is { } after) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var schedules = await query.OrderBy(x => x.Key).Take(request.MaximumOccurrences).ToArrayAsync(ct);
            var receipts = new List<JobOccurrenceReceipt>();
            var madeProgress = false;
            var recurrenceUnavailable = false;
            var visited = 0;
            foreach (var schedule in schedules)
            {
                ct.ThrowIfCancellationRequested();
                cursor.AfterKey = schedule.Key;
                visited++;
                JobScheduleDefinition definition; SchedulePlan plan;
                try
                {
                    definition = Definition(request.Scope, schedule);
                    plan = Plan(schedule, definition, now, request.MaximumOccurrences - receipts.Count, ct);
                }
                catch (JobException error) when (error.Code == "jobs.recurrence.unavailable")
                {
                    // Commit discovery progress, not a permanent quarantine. Reinstallation can retry
                    // this exact owed cursor; another schedule in the same scope can still progress.
                    recurrenceUnavailable = true; continue;
                }
                catch (Exception error) when (InvalidSchedule(error))
                {
                    schedule.UnavailableDiagnosticJson = JobStoreCodec.Json(ScheduleUnavailable());
                    schedule.Revision = checked(schedule.Revision + 1); schedule.UpdatedAt = now;
                    madeProgress = true; continue;
                }
                var gates = await Gates(db, request.Scope, JobStoreCodec.Gates(definition.Template), ct);
                if (await Availability(db, gates, now, permits: false, ct) is not null) continue;
                Touch(gates);
                if (plan.Counting)
                {
                    schedule.CoalesceNext = plan.CoalesceNext; schedule.CoalesceCutoff = plan.Cutoff;
                    schedule.CoalesceCount = plan.Count; schedule.CoalesceThrough = plan.Through;
                    schedule.Revision = checked(schedule.Revision + 1); schedule.UpdatedAt = now;
                }
                else
                {
                    foreach (var occurrence in plan.Occurrences)
                        receipts.Add(await Materialize(db, request.Scope, schedule, definition, occurrence.Due,
                            occurrence.Count, occurrence.Through, now, ct));
                    schedule.CoalesceNext = null; schedule.CoalesceCutoff = null; schedule.CoalesceCount = 0; schedule.CoalesceThrough = null;
                    Advance(schedule, plan.Next, now);
                }
                madeProgress = true;
                if (receipts.Count >= request.MaximumOccurrences) break;
            }
            // A replay schedule can consume the occurrence budget before later fetched rows are visited.
            // Preserve that discovery position until the fetched page and the query are both exhausted.
            if (visited == schedules.Length && schedules.Length < request.MaximumOccurrences) cursor.AfterKey = null;
            return new JobMaterializationResult(madeProgress ? JobCommandStatus.Applied : recurrenceUnavailable ? JobCommandStatus.Unavailable : JobCommandStatus.NoEligibleWork, receipts);
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
