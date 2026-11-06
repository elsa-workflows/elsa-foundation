using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Api.Models;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api.Services;

internal sealed partial class JobsOperations(JobsRequestAccess access, IJobCommands commands, IJobQueries queries, JobsPageCursors cursors,
    IEnumerable<IJobDataPolicy> dataPolicies)
{
    internal async ValueTask<JobApiAccessView> AccessAsync(CancellationToken ct)
    {
        var actor = access.Actor(); var granted = new List<string>();
        foreach (var permission in JobsPermissions.All)
            if (await access.CanAsync(actor, permission, null, ct)) granted.Add(permission);
        return new(actor.Scope.TenantId!, actor.SubjectId, granted);
    }

    internal async ValueTask<JobApiPage<JobView>> ListAsync(JobListRequest request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Read, ct);
        OptionalName(request.Queue); OptionalName(request.JobType);
        if (request.State is { } state && !Enum.IsDefined(state)) throw new ArgumentException("Unknown job state.");
        var cursor = cursors.Bind(actor, request.Limit, "jobs", request.Queue, request.JobType, request.State?.ToString());
        var page = await queries.ListJobsAsync(actor.Scope, new(cursor.Request(request.After), request.Queue, request.JobType, request.State), ct);
        return await ProjectAsync(actor, page, cursor, x => Resource(actor, x), JobView.From, ct);
    }

    internal async ValueTask<JobView> GetAsync(string jobId, CancellationToken ct)
    {
        var actor = access.Actor(); var job = await JobAsync(actor, jobId, JobsPermissions.Read, ct);
        var view = JobView.From(job);
        if ((job.RequestMetadata?.Association ?? job.Request?.Association)?.Mode != JobAssociationMode.Awaited) return view;
        var delivery = await queries.FindDeliveryAsync(actor.Scope, jobId, ct);
        if (delivery is null) return view with { ConsumptionState = job.ResultMetadata is null && job.Result is null ? "pending-result" : "unknown" };
        if (delivery.JobId != jobId || delivery.ResultId != (job.ResultMetadata?.ResultId ?? job.Result?.ResultId)) throw Unavailable();
        return view with { ConsumptionState = delivery.IsAcknowledged ? "settled" : "owed" };
    }

    internal async ValueTask<JobAcceptance> SubmitAsync(SubmitJobRequest request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Submit, ct); Name(request.IdempotencyKey);
        ArgumentNullException.ThrowIfNull(request.Invocation);
        var invocation = request.Invocation.ToRequest();
        await access.RequireAsync(actor, JobsPermissions.Submit, new(actor.Scope.TenantId!, "submission", request.IdempotencyKey, invocation.Queue, invocation.Handler.JobType), ct);
        foreach (var policy in dataPolicies) policy.ValidateAdmission(actor.Scope, invocation);
        var result = await commands.AdmitAsync(new(actor.Scope, Guid.NewGuid().ToString("N"), request.IdempotencyKey,
            JobRequestDigest.Compute(invocation), invocation, null), ct);
        RequireApplied(result.Status);
        return result.Acceptance ?? throw Unavailable();
    }

    internal async ValueTask<JobInputView> InputAsync(string jobId, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.InputsRead, ct);
        var job = await JobAsync(actor, jobId, JobsPermissions.Read, ct);
        await access.RequireAsync(actor, JobsPermissions.InputsRead, Resource(actor, job), ct, conceal: true);
        if (job.UnavailableDiagnostic is not null) throw Unavailable();
        if (job.InputPayloadState == JobPayloadState.Expired && job.RequestMetadata is { } metadata && job.PayloadRetention?.InputExpiredAt is { } expired)
            return new("expired", null, Schema: metadata.InputSchema, PayloadSha256: metadata.InputSha256, ExpiredAt: expired);
        if (job.Request is null) throw Unavailable();
        return new("available", job.Request.Input, PayloadValueJson(job.Request.Input));
    }

    internal async ValueTask<JobResultView> ResultAsync(string jobId, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.ResultsRead, ct);
        var job = await JobAsync(actor, jobId, JobsPermissions.Read, ct);
        await access.RequireAsync(actor, JobsPermissions.ResultsRead, Resource(actor, job), ct, conceal: true);
        if (job.UnavailableDiagnostic is not null) throw Unavailable();
        if (job.ResultPayloadState == JobPayloadState.Expired && job.ResultMetadata is { } metadata && job.PayloadRetention?.ResultExpiredAt is { } expired)
            return new("expired", metadata.ResultId, metadata.Outcome, null, metadata.CompletedAt,
                Schema: metadata.PayloadSchema, PayloadSha256: metadata.PayloadSha256, ExpiredAt: expired);
        return job.Result is { } result ? new("available", result.ResultId, result.Outcome, result.Payload, result.CompletedAt, PayloadValueJson(result.Payload))
            : new("pending", null, null, null, null);
    }

    internal async ValueTask<JobApiPage<JobAttemptView>> AttemptsAsync(string jobId, JobPageQuery request, CancellationToken ct)
    {
        var actor = access.Actor(); await JobAsync(actor, jobId, JobsPermissions.Read, ct);
        var cursor = cursors.Bind(actor, request.Limit, "attempts", jobId);
        var page = await queries.ListAttemptsAsync(actor.Scope, jobId, cursor.Request(request.After), ct);
        return new(page.Items.Select(JobAttemptView.From).ToArray(), cursor.Next(page.ContinuationToken));
    }

    internal async ValueTask<JobApiPage<JobPhysicalReceiptView>> PhysicalReceiptsAsync(string jobId, JobPageQuery request, CancellationToken ct)
    {
        var actor = access.Actor(); await JobAsync(actor, jobId, JobsPermissions.Read, ct);
        var cursor = cursors.Bind(actor, request.Limit, "physical-receipts", jobId);
        var page = await queries.ListPhysicalReceiptInspectionsAsync(actor.Scope, jobId, cursor.Request(request.After), ct);
        return new(page.Items.Select(JobPhysicalReceiptView.From).ToArray(), cursor.Next(page.ContinuationToken));
    }

    internal async ValueTask<JobTransition> CancelAsync(string jobId, JobOperationRequest request, CancellationToken ct)
    {
        var actor = access.Actor(); await JobAsync(actor, jobId, JobsPermissions.Cancel, ct);
        return Applied(await commands.CancelAsync(new(actor.Scope, jobId, Operation(actor, request.OperationId, request.Reason)), ct));
    }

    internal async ValueTask<JobAcceptance> RetryAsync(string jobId, RetryJobRequest request, CancellationToken ct)
    {
        var actor = access.Actor(); await JobAsync(actor, jobId, JobsPermissions.Retry, ct);
        Name(request.IdempotencyKey); Revision(request.ExpectedRevision);
        var result = await commands.RetryAsNewRunAsync(new(actor.Scope, jobId, request.ExpectedRevision,
            Guid.NewGuid().ToString("N"), request.IdempotencyKey, Operation(actor, request.OperationId, request.Reason)), ct);
        RequireApplied(result.Status); return result.Acceptance ?? throw Unavailable();
    }

    internal async ValueTask<JobApiPage<JobQueueSnapshot>> QueuesAsync(JobPageQuery request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Read, ct);
        var cursor = cursors.Bind(actor, request.Limit, "queues");
        var page = await queries.ListQueuesAsync(actor.Scope, cursor.Request(request.After), ct);
        return await ProjectAsync(actor, page, cursor, x => new(actor.Scope.TenantId!, "queue", x.Name, x.Name), x => x, ct);
    }

    internal async ValueTask<JobApiPage<JobScheduleView>> SchedulesAsync(JobPageQuery request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Read, ct);
        var cursor = cursors.Bind(actor, request.Limit, "schedules");
        var page = await queries.ListSchedulesAsync(actor.Scope, cursor.Request(request.After), ct);
        return await ProjectAsync(actor, page, cursor, x => ScheduleResource(actor, x), JobScheduleView.From, ct);
    }

    internal async ValueTask<JobScheduleView> ScheduleAsync(string scheduleId, CancellationToken ct)
    {
        var actor = access.Actor(); return JobScheduleView.From(await ScheduleAsync(actor, scheduleId, JobsPermissions.Read, ct));
    }

    internal async ValueTask<JobInputView> ScheduleInputAsync(string scheduleId, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.InputsRead, ct);
        var schedule = await ScheduleAsync(actor, scheduleId, JobsPermissions.Read, ct);
        await access.RequireAsync(actor, JobsPermissions.InputsRead, ScheduleResource(actor, schedule), ct, conceal: true);
        if (schedule.Definition is null || schedule.UnavailableDiagnostic is not null) throw Unavailable();
        return new("available", schedule.Definition.Template.Input, PayloadValueJson(schedule.Definition.Template.Input));
    }

    internal async ValueTask<JobTransition> SaveScheduleAsync(string scheduleId, SaveJobScheduleRequest request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.SchedulesManage, ct); Name(scheduleId); Revision(request.ExpectedRevision);
        ArgumentNullException.ThrowIfNull(request.Invocation); ArgumentNullException.ThrowIfNull(request.Recurrence);
        var prior = await queries.FindScheduleAsync(actor.Scope, scheduleId, ct);
        if (prior is not null)
        {
            await access.RequireAsync(actor, JobsPermissions.SchedulesManage, ScheduleResource(actor, prior), ct, conceal: true);
            // A retained, valid definition may outlive arithmetic availability. The provider recognizes
            // committed receipts before using today's calculator; malformed retained data still blocks edits.
            if (prior.Definition is null || prior.UnavailableDiagnostic is { Code: not "jobs.recurrence.unavailable" }) throw Unavailable();
        }
        // Existing trusted model association is preserved by the server. It is never accepted from a caller.
        var template = request.Invocation.ToRequest(prior?.Definition?.Template.Association);
        var resource = new JobApiResource(actor.Scope.TenantId!, "schedule", scheduleId, template.Queue, template.Handler.JobType);
        await access.RequireAsync(actor, JobsPermissions.SchedulesManage, resource, ct);
        foreach (var policy in dataPolicies) policy.ValidateAdmission(actor.Scope, template);
        return Applied(await commands.SaveScheduleAsync(new(actor.Scope,
            new(scheduleId, template, request.Recurrence, request.FirstOccurrence, request.MissedPolicy), request.ExpectedRevision,
            Operation(actor, request.OperationId, request.Reason)), ct));
    }

    internal async ValueTask<JobTransition> EnableScheduleAsync(string scheduleId, EnableJobScheduleRequest request, CancellationToken ct)
    {
        var actor = access.Actor(); await ScheduleAsync(actor, scheduleId, JobsPermissions.SchedulesManage, ct); Revision(request.ExpectedRevision);
        return Applied(await commands.SetScheduleEnabledAsync(new(actor.Scope, scheduleId, request.Enabled, request.ExpectedRevision,
            Operation(actor, request.OperationId, request.Reason)), ct));
    }

    internal async ValueTask<JobApiPage<JobHold>> HoldsAsync(JobHoldQuery request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Read, ct);
        JobGateKey? gate = request.Kind is { } kind ? Gate(new(kind, request.Name ?? "", request.ModelKind)) : null;
        if (gate is null && (request.Name is not null || request.ModelKind is not null)) throw new ArgumentException("A complete gate filter is required.");
        var cursor = HoldCursor(actor, request.Limit, gate);
        var page = await queries.ListHoldsAsync(actor.Scope, gate, cursor.Request(request.After), ct);
        return await ProjectAsync(actor, page, cursor, x => GateResource(actor, x.Gate), x => x, ct);
    }

    internal async ValueTask<JobHoldsView> JobHoldsAsync(string jobId, JobPageQuery request, CancellationToken ct)
    {
        if (request.After is not null) throw new ArgumentException("Continue each gate through the holds endpoint.");
        var actor = access.Actor(); var job = await JobAsync(actor, jobId, JobsPermissions.Read, ct);
        var invocation = job.RequestMetadata ?? (job.Request is { } retained ? JobRequestMetadata.From(retained) : null);
        if (invocation is null || job.UnavailableDiagnostic is not null) throw Unavailable();
        var gates = new List<JobGateKey> { new(JobGateKind.Queue, invocation.Queue), new(JobGateKind.JobType, invocation.Handler.JobType) };
        if (invocation.Association is { } association)
        {
            gates.Add(new(JobGateKind.WorkflowInstance, association.ExecutionId, association.ModelKind));
            gates.Add(new(JobGateKind.WorkflowDefinition, association.DefinitionId, association.ModelKind));
        }
        var result = new List<JobGateHoldsView>();
        foreach (var gate in gates)
        {
            await access.RequireAsync(actor, JobsPermissions.Read, GateResource(actor, gate), ct, conceal: true);
            var cursor = HoldCursor(actor, request.Limit, gate);
            var page = await queries.ListHoldsAsync(actor.Scope, gate, cursor.Request(null), ct);
            result.Add(new(gate, page.Items, cursor.Next(page.ContinuationToken)));
        }
        return new(result, result.All(x => x.Next is null));
    }

    internal async ValueTask<JobTransition> PauseAsync(PauseJobsRequest request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Pause, ct); var gate = Gate(request.Gate); Name(request.HoldId);
        await access.RequireAsync(actor, JobsPermissions.Pause, GateResource(actor, gate), ct);
        return Applied(await commands.PauseAsync(new(actor.Scope, gate, request.HoldId, Operation(actor, request.OperationId, request.Reason)), ct));
    }

    internal async ValueTask<JobTransition> ReleaseAsync(string holdId, ReleaseJobHoldRequest request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Pause, ct); var gate = Gate(request.Gate); Name(holdId); Revision(request.ExpectedRevision);
        await access.RequireAsync(actor, JobsPermissions.Pause, GateResource(actor, gate), ct);
        return Applied(await commands.ReleaseHoldAsync(new(actor.Scope, gate, holdId, request.ExpectedRevision,
            Operation(actor, request.OperationId, request.Reason)), ct));
    }

    internal async ValueTask<JobApiPage<JobCapacity>> CapacityAsync(JobPageQuery request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.Read, ct);
        var cursor = cursors.Bind(actor, request.Limit, "capacity");
        return await ProjectAsync(actor, await queries.ListCapacityAsync(actor.Scope, cursor.Request(request.After), ct), cursor,
            x => GateResource(actor, x.Gate), x => x, ct);
    }

    internal async ValueTask<JobTransition> SetCapacityAsync(SetJobCapacityRequest request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.CapacityManage, ct); var gate = Gate(request.Gate); Revision(request.ExpectedRevision);
        await access.RequireAsync(actor, JobsPermissions.CapacityManage, GateResource(actor, gate), ct);
        return Applied(await commands.SetCapacityAsync(new(actor.Scope, gate, request.Limit, request.ExpectedRevision,
            Operation(actor, request.OperationId, request.Reason)), ct));
    }

    private async ValueTask<JobApiActor> RequireAsync(string permission, CancellationToken ct)
    {
        var actor = access.Actor(); await access.RequireAsync(actor, permission, null, ct); return actor;
    }
    private async ValueTask<JobSnapshot> JobAsync(JobApiActor actor, string jobId, string permission, CancellationToken ct)
    {
        Name(jobId); await access.RequireAsync(actor, permission, null, ct);
        var job = await queries.FindAsync(actor.Scope, jobId, ct) ?? throw Missing();
        await access.RequireAsync(actor, permission, Resource(actor, job), ct, conceal: true); return job;
    }
    private async ValueTask<JobScheduleSnapshot> ScheduleAsync(JobApiActor actor, string id, string permission, CancellationToken ct)
    {
        Name(id); await access.RequireAsync(actor, permission, null, ct);
        var schedule = await queries.FindScheduleAsync(actor.Scope, id, ct) ?? throw Missing();
        await access.RequireAsync(actor, permission, ScheduleResource(actor, schedule), ct, conceal: true); return schedule;
    }
    private async ValueTask<JobApiPage<TOut>> ProjectAsync<TIn, TOut>(JobApiActor actor, JobPage<TIn> page, JobApiPageCursor cursor,
        Func<TIn, JobApiResource> resource, Func<TIn, TOut> project, CancellationToken ct)
    {
        var result = new List<TOut>();
        foreach (var value in page.Items)
            if (await access.CanAsync(actor, JobsPermissions.Read, resource(value), ct)) result.Add(project(value));
        return new(result, cursor.Next(page.ContinuationToken));
    }
    private static JobApiResource Resource(JobApiActor actor, JobSnapshot job) =>
        new(actor.Scope.TenantId!, "job", job.JobId, job.RequestMetadata?.Queue ?? job.Request?.Queue, job.RequestMetadata?.Handler.JobType ?? job.Request?.Handler.JobType);
    private static JobApiResource ScheduleResource(JobApiActor actor, JobScheduleSnapshot schedule) =>
        new(actor.Scope.TenantId!, "schedule", schedule.ScheduleId, schedule.Definition?.Template.Queue, schedule.Definition?.Template.Handler.JobType);
    private static JobApiResource GateResource(JobApiActor actor, JobGateKey gate) => new(actor.Scope.TenantId!, "gate", gate.Name,
        gate.Kind == JobGateKind.Queue ? gate.Name : null, gate.Kind == JobGateKind.JobType ? gate.Name : null, gate);
    private JobApiPageCursor HoldCursor(JobApiActor actor, int limit, JobGateKey? gate) =>
        cursors.Bind(actor, limit, "holds", gate?.Kind.ToString(), gate?.Name, gate?.ModelKind);
    private static JobOperation Operation(JobApiActor actor, string id, string reason) { Name(id); Name(reason); return new(id, actor.SubjectId, reason); }
    private static JobGateKey Gate(JobGateKey gate)
    {
        ArgumentNullException.ThrowIfNull(gate); Name(gate.Name);
        if (!Enum.IsDefined(gate.Kind) || gate.Kind is JobGateKind.WorkflowDefinition or JobGateKind.WorkflowInstance && string.IsNullOrWhiteSpace(gate.ModelKind)
            || gate.Kind is JobGateKind.Queue or JobGateKind.JobType && gate.ModelKind is not null) throw new ArgumentException("A complete valid gate is required.");
        OptionalName(gate.ModelKind); return gate;
    }
    private static void OptionalName(string? value) { if (value is not null) Name(value); }
    private static void Name(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("A bounded nonblank identity is required."); }
    private static void Revision(long value) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static JobTransition Applied(JobTransition transition) { RequireApplied(transition.Status); return transition; }
    private static void RequireApplied(JobCommandStatus status)
    {
        if (status is JobCommandStatus.Applied or JobCommandStatus.Replayed) return;
        throw status == JobCommandStatus.NotFound ? Missing()
            : status == JobCommandStatus.Unavailable ? Unavailable()
            : status == JobCommandStatus.PayloadExpired ? new JobsApiException(409, "jobs.payload.expired", "The original Jobs input has expired and cannot be retried as a new run.")
            : new JobsApiException(409, "jobs.operation.conflict", "The Jobs authority refused this operation; reload its current state.");
    }
    private static string? PayloadValueJson(JobPayload? payload)
    {
        if (payload is null) return null;
        var json = payload.Value.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > JobPayloadLimits.MaximumBytes) throw Unavailable();
        return json;
    }
    private static JobsApiException Missing() => new(404, "jobs.not-found", "The requested Jobs resource was not found.");
    private static JobsApiException Unavailable() => new(503, "jobs.record.unavailable", "The requested Jobs state is unavailable.");
}
