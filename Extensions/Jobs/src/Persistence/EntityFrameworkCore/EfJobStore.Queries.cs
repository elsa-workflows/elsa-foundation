using System.Data.Common;
using System.Text;
using Elsa.Jobs.Core;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal sealed partial class EfJobStore
{
    private async ValueTask<T> Read<T>(string operation, Func<JobsDbContext, CancellationToken, ValueTask<T>> read, CancellationToken ct)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            return await read(db, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (JobException error) when (JobStoreCodec.SerializationFailure(error))
        { throw new JobException("jobs.persistence.invalid-record", "The retained Jobs record is unavailable to this provider.", error); }
        catch (JobException) { throw; }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        { throw new JobException("jobs.persistence.invalid-record", "The retained Jobs record is unavailable to this provider.", error); }
        catch (Exception error) when (error is DbException or DbUpdateException || EfRelationalExceptionClassifier.IsWrappedProviderFailure(error))
        { throw new JobPersistenceException(operation, error); }
    }

    public ValueTask<JobSnapshot?> FindAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default) =>
        Read<JobSnapshot?>("find", async (db, ct) =>
        {
            JobStoreCodec.Name(jobId);
            var key = JobStoreCodec.Key(scope, "job", jobId);
            var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
            return job is null ? null : await Snapshot(db, scope, job, ct);
        }, cancellationToken);

    private static async ValueTask<JobSnapshot> Snapshot(JobsDbContext db, JobScope scope, JobRecord job, CancellationToken ct)
    {
        JobStoreCodec.ExactIdentity(job, scope, "job", job.JobId);
        var active = job.ActiveAttemptKey is { } key ? await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct) : null;
        JobRequest? content = null; JobResult? result = null; JobProgress? progress = null; JobDiagnostic? unavailable = null;
        JobRequestMetadata? requestMetadata = null; JobResultMetadata? resultMetadata = null; JobPayloadRetention? retention = null;
        try
        {
            if (job.UnavailableDiagnosticJson is { } diagnostic) unavailable = JobStoreCodec.ReadRetained<JobDiagnostic>(diagnostic);
            if (job.SchemaVersion != JobsEfModule.Version) throw new NotSupportedException("The Jobs record schema is unavailable.");
            requestMetadata = RequestMetadata(job); resultMetadata = ResultMetadata(job, requestMetadata);
            retention = Retention(job, resultMetadata);
            content = job.InputExpiredAt is null ? Request(job) : null;
            result = job.ResultJson is { } resultJson ? JobStoreCodec.ReadRetained<JobResult>(resultJson) : null;
            progress = job.ProgressJson is { } progressJson ? JobStoreCodec.ReadRetained<JobProgress>(progressJson) : null;
            if (progress?.Data is { } progressData) JobStoreCodec.Payload(progressData);
            if (progress is not null && System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(progress).Length > JobPayloadLimits.MaximumProgressBytes)
                throw new InvalidDataException("The retained Jobs progress exceeds its durable envelope bound.");
            if (active is not null) JobStoreCodec.Exact(active, scope, "attempt", job.JobId, active.AttemptId);
        }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or NotSupportedException or ArgumentException or OverflowException || JobStoreCodec.SerializationFailure(error))
        {
            content = null; result = null; progress = null; active = null; requestMetadata = null; resultMetadata = null; retention = null;
            unavailable = new("jobs.record.unavailable", "Retained execution data is unavailable; its identity and original bytes remain protected.");
        }
        return new(scope, job.JobId, job.RequestSha256, content, (JobState)job.State, job.Revision,
            JobStoreCodec.At(job.AcceptedAt), JobStoreCodec.At(job.EligibleAt), active is null ? null : Lease(scope, job, active),
            result, progress, job.CancellationRequested, job.RetriedFromJobId, unavailable)
        {
            RequestMetadata = requestMetadata, ResultMetadata = resultMetadata, PayloadRetention = retention,
            InputPayloadState = retention?.InputExpiredAt is not null ? JobPayloadState.Expired : JobPayloadState.Available,
            ResultPayloadState = retention?.ResultExpiredAt is not null ? JobPayloadState.Expired
                : resultMetadata is null ? JobPayloadState.NotProduced : JobPayloadState.Available
        };
    }

    private sealed record Cursor(string Scope, string Category, string Filter, string After);
    private string? After(JobScope scope, string category, string filter, JobPageRequest page)
    {
        if (page.Limit is < 1 || page.Limit > settings.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(page));
        if (page.ContinuationToken is not { } token) return null;
        if (token.Length > 4096) throw new ArgumentException("The Jobs cursor exceeds its bound.");
        try
        {
            var cursor = JobStoreCodec.Read<Cursor>(Encoding.UTF8.GetString(Convert.FromBase64String(token)));
            if (cursor.Scope != JobStoreCodec.Scope(scope) || cursor.Category != category || cursor.Filter != filter) throw new ArgumentException("The Jobs cursor belongs to different scope or filters.");
            JobStoreCodec.Name(cursor.After);
            if (category is not ("attempts" or "queues")) JobStoreCodec.Digest(cursor.After);
            return cursor.After;
        }
        catch (Exception error) when (error is FormatException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        { throw new ArgumentException("The Jobs cursor is malformed or belongs to different scope or filters.", nameof(page), error); }
    }
    private static string CursorFor(JobScope scope, string category, string filter, string after) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JobStoreCodec.Json(new Cursor(JobStoreCodec.Scope(scope), category, filter, after))));
    private static JobPage<T> Page<T>(IReadOnlyList<T> values, int limit, JobScope scope, string category, string filter, Func<T, string> key) =>
        new(values.Take(limit).ToArray(), values.Count > limit ? CursorFor(scope, category, filter, key(values[limit - 1])) : null);

    public ValueTask<JobPage<JobSnapshot>> ListJobsAsync(JobScope scope, JobListQuery query, CancellationToken cancellationToken = default)
    {
        var filter = JobStoreCodec.Hash(JobStoreCodec.Json(new { query.Queue, query.JobType, query.State }));
        var after = After(scope, "jobs", filter, query.Page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-jobs", async (db, ct) =>
        {
            var rows = db.Jobs.AsNoTracking().Where(x => x.ScopeKey == scopeKey);
            if (query.Queue is { } queue) rows = rows.Where(x => x.Queue == queue);
            if (query.JobType is { } type) rows = rows.Where(x => x.JobType == type);
            if (query.State is { } state) rows = rows.Where(x => x.State == (int)state);
            if (after is not null) rows = rows.Where(x => string.Compare(x.Key, after) > 0);
            var found = await rows.OrderBy(x => x.Key).Take(query.Page.Limit + 1).ToArrayAsync(ct);
            var items = new List<JobSnapshot>();
            foreach (var row in found.Take(query.Page.Limit)) items.Add(await Snapshot(db, scope, row, ct));
            return new JobPage<JobSnapshot>(items, found.Length > query.Page.Limit ? CursorFor(scope, "jobs", filter, found[query.Page.Limit - 1].Key) : null);
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobAttemptSnapshot>> ListAttemptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "attempts", jobId, page);
        var number = 0;
        if (after is not null && (after.Length > 4 || !int.TryParse(after, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number) || number is < 1 or > 1000))
            throw new ArgumentException("The Jobs attempt cursor is invalid.", nameof(page), new FormatException("A Jobs attempt cursor must identify a bounded positive attempt number."));
        return Read("list-attempts", async (db, ct) =>
        {
            var job = await Find(db, scope, jobId, ct);
            if (job is null) return new JobPage<JobAttemptSnapshot>([], null);
            var rows = await db.Attempts.AsNoTracking().Where(x => x.JobKey == job.Key && x.Number > number).OrderBy(x => x.Number).Take(page.Limit + 1).ToArrayAsync(ct);
            var items = rows.Select(row =>
            {
                JobStoreCodec.Exact(row, scope, "attempt", job.JobId, row.AttemptId);
                return new JobAttemptSnapshot(Lease(scope, job, row), (JobAttemptState)row.State, row.PhysicalStartedAt is { } started ? JobStoreCodec.At(started) : null,
                    row.PhysicalExitedAt is { } exited ? JobStoreCodec.At(exited) : null, row.DiagnosticJson is { } diagnostic ? JobStoreCodec.ReadRetained<JobDiagnostic>(diagnostic) : null);
            }).ToArray();
            return Page(items, page.Limit, scope, "attempts", jobId, x => x.Lease.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobPhysicalReceipt>> ListPhysicalReceiptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        return FullReceiptsAsync(scope, jobId, page, cancellationToken);
    }

    private async ValueTask<JobPage<JobPhysicalReceipt>> FullReceiptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken)
    {
        var inspections = await ListPhysicalReceiptInspectionsAsync(scope, jobId, page, cancellationToken);
        if (inspections.Items.Any(item => item.PayloadState == JobPayloadState.Expired))
            throw new JobException("jobs.payload.expired", "The original Jobs physical-report payload has expired; retained report metadata remains available.");
        return new(inspections.Items.Select(item => item.Receipt ?? throw InvalidRetention()).ToArray(), inspections.ContinuationToken);
    }

    public ValueTask<JobPage<JobPhysicalReceiptInspection>> ListPhysicalReceiptInspectionsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "receipts", jobId, page);
        return Read("list-receipts", async (db, ct) =>
        {
            var job = await Find(db, scope, jobId, ct);
            if (job is null) return new JobPage<JobPhysicalReceiptInspection>([], null);
            var metadata = RequestMetadata(job); _ = Retention(job, ResultMetadata(job, metadata));
            var query = db.Receipts.AsNoTracking().Where(x => x.JobKey == job.Key);
            if (after is not null) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var rows = await query.OrderBy(x => x.Key).Take(page.Limit + 1).ToArrayAsync(ct);
            var items = rows.Take(page.Limit).Select(row => ReceiptInspection(scope, job, row)).ToArray();
            return new JobPage<JobPhysicalReceiptInspection>(items, rows.Length > page.Limit ? CursorFor(scope, "receipts", jobId, rows[page.Limit - 1].Key) : null);
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobHold>> ListHoldsAsync(JobScope scope, JobGateKey? gate, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var filter = JobStoreCodec.Hash(JobStoreCodec.Json(gate)); var after = After(scope, "holds", filter, page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-holds", async (db, ct) =>
        {
            var query = db.Holds.AsNoTracking().Where(x => x.ScopeKey == scopeKey);
            if (gate is not null) { JobStoreCodec.Gate(gate); var gateKey = JobStoreCodec.Key(scope, "gate", gate.Kind, gate.Name, gate.ModelKind); query = query.Where(x => x.GateKey == gateKey); }
            if (after is not null) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var rows = await query.OrderBy(x => x.Key).Take(page.Limit + 1).ToArrayAsync(ct);
            var items = rows.Take(page.Limit).Select(x => JobStoreCodec.ReadRetained<JobHold>(x.HoldJson)).ToArray();
            return new JobPage<JobHold>(items, rows.Length > page.Limit ? CursorFor(scope, "holds", filter, rows[page.Limit - 1].Key) : null);
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobCapacity>> ListCapacityAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "capacity", "", page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-capacity", async (db, ct) =>
        {
            var query = db.Gates.AsNoTracking().Where(x => x.ScopeKey == scopeKey && x.CapacityRevision > 0);
            if (after is not null) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var rows = await query.OrderBy(x => x.Key).Take(page.Limit + 1).ToArrayAsync(ct);
            return new JobPage<JobCapacity>(rows.Take(page.Limit).Select(x => new JobCapacity(new((JobGateKind)x.Kind, x.Name, x.ModelKind), x.Capacity, x.CapacityRevision)).ToArray(),
                rows.Length > page.Limit ? CursorFor(scope, "capacity", "", rows[page.Limit - 1].Key) : null);
        }, cancellationToken);
    }

    private JobScheduleSnapshot Schedule(JobScope scope, JobScheduleRecord row)
    {
        var id = ScheduleId(scope, row);
        JobScheduleDefinition? definition = null; JobDiagnostic? unavailable = null;
        if (row.SchemaVersion != JobsEfModule.Version)
            unavailable = new("jobs.schedule.schema-unavailable", "This provider cannot interpret the retained schedule schema; its identity and owed cursor remain protected.");
        else if (row.UnavailableDiagnosticJson is not null) unavailable = ScheduleUnavailable();
        else
        {
            try
            {
                definition = Definition(scope, row);
                _ = Next(definition.Recurrence, JobStoreCodec.At(row.NextOccurrence));
            }
            catch (JobException error) when (error.Code == "jobs.recurrence.unavailable") { unavailable = RecurrenceUnavailable(); }
            catch (Exception error) when (InvalidSchedule(error)) { definition = null; unavailable = ScheduleUnavailable(); }
        }
        return new(definition, row.Revision, JobStoreCodec.At(row.NextOccurrence), row.Enabled,
            JobStoreCodec.At(row.CreatedAt), JobStoreCodec.At(row.UpdatedAt), id, unavailable);
    }

    public ValueTask<JobScheduleSnapshot?> FindScheduleAsync(JobScope scope, string scheduleId, CancellationToken cancellationToken = default) =>
        Read<JobScheduleSnapshot?>("find-schedule", async (db, ct) =>
        {
            JobStoreCodec.Name(scheduleId);
            var key = JobStoreCodec.Key(scope, "schedule", scheduleId);
            var row = await db.Schedules.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
            return row is null ? null : Schedule(scope, row);
        }, cancellationToken);

    public ValueTask<JobPage<JobScheduleSnapshot>> ListSchedulesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "schedules", "", page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-schedules", async (db, ct) =>
        {
            var query = db.Schedules.AsNoTracking().Where(x => x.ScopeKey == scopeKey);
            if (after is not null) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var rows = await query.OrderBy(x => x.Key).Take(page.Limit + 1).ToArrayAsync(ct);
            return new JobPage<JobScheduleSnapshot>(rows.Take(page.Limit).Select(row => Schedule(scope, row)).ToArray(), rows.Length > page.Limit ? CursorFor(scope, "schedules", "", rows[page.Limit - 1].Key) : null);
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobDeliverySnapshot>> ListPendingDeliveriesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "deliveries", "", page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-deliveries", async (db, ct) =>
        {
            var query = db.Deliveries.AsNoTracking().Where(x => x.ScopeKey == scopeKey && !x.Acknowledged);
            if (after is not null) query = query.Where(x => string.Compare(x.Key, after) > 0);
            var rows = await query.OrderBy(x => x.Key).Take(page.Limit + 1).ToArrayAsync(ct);
            var items = new List<JobDeliverySnapshot>();
            foreach (var row in rows.Take(page.Limit))
            {
                var job = await db.Jobs.AsNoTracking().SingleAsync(x => x.Key == row.JobKey, ct);
                JobStoreCodec.Exact(job, scope, "job", job.JobId); JobStoreCodec.Exact(row, scope, "delivery", job.JobId);
                items.Add(new(job.JobId, row.ResultId, JobStoreCodec.ReadRetained<JobAssociation>(row.AssociationJson), row.PermissionJson is { } json ? JobStoreCodec.ReadRetained<JobContinuationPermission>(json) : null));
            }
            return new JobPage<JobDeliverySnapshot>(items, rows.Length > page.Limit ? CursorFor(scope, "deliveries", "", rows[page.Limit - 1].Key) : null);
        }, cancellationToken);
    }

    public ValueTask<JobPage<JobQueueSnapshot>> ListQueuesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default)
    {
        var after = After(scope, "queues", "", page); var scopeKey = JobStoreCodec.Scope(scope);
        return Read("list-queues", async (db, ct) =>
        {
            var names = db.Jobs.AsNoTracking().Where(x => x.ScopeKey == scopeKey).Select(x => x.Queue).Distinct();
            if (after is not null) names = names.Where(x => string.Compare(x, after) > 0);
            var queues = await names.OrderBy(x => x).Take(page.Limit + 1).ToArrayAsync(ct);
            var now = await db.ReadUtcTicksAsync(ct); var result = new List<JobQueueSnapshot>();
            foreach (var queue in queues)
            {
                var jobs = db.Jobs.AsNoTracking().Where(x => x.ScopeKey == scopeKey && x.Queue == queue);
                var active = await jobs.LongCountAsync(x => x.ActiveAttemptKey != null && db.Attempts.Any(a => a.Key == x.ActiveAttemptKey && a.ExpiresAt > now), ct);
                var held = await jobs.LongCountAsync(x => x.State != (int)JobState.Terminal
                    && (x.ActiveAttemptKey == null || !db.Attempts.Any(a => a.Key == x.ActiveAttemptKey && a.ExpiresAt > now))
                    && db.Memberships.Any(m => m.JobKey == x.Key && db.Gates.Any(g => g.Key == m.GateKey && g.ActiveHolds > 0)), ct);
                result.Add(new(queue, await jobs.LongCountAsync(ct), active, held));
            }
            return Page(result, page.Limit, scope, "queues", "", x => x.Name);
        }, cancellationToken);
    }
}
