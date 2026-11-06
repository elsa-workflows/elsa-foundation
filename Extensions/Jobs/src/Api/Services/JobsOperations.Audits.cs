using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Api.Models;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api.Services;

internal sealed partial class JobsOperations
{
    internal async ValueTask<JobApiPage<JobOperationAuditView>> OperationAuditsAsync(JobPageQuery request, CancellationToken ct)
    {
        var actor = await RequireAsync(JobsPermissions.AuditRead, ct);
        var cursor = cursors.Bind(actor, request.Limit, "operation-audits");
        var page = await queries.ListOperationAuditsAsync(actor.Scope, cursor.Request(request.After), ct);
        var result = new List<JobOperationAuditView>();
        foreach (var value in page.Items)
            if (await CanInspectAuditAsync(actor, value, ct)) result.Add(JobOperationAuditView.From(value));
        return new(result, cursor.Next(page.ContinuationToken));
    }

    internal async ValueTask<JobOperationAuditView> OperationAuditAsync(string operationId, CancellationToken ct)
    {
        Name(operationId);
        var actor = await RequireAsync(JobsPermissions.AuditRead, ct);
        var audit = await queries.FindOperationAuditAsync(actor.Scope, operationId, ct) ?? throw Missing();
        if (!await CanInspectAuditAsync(actor, audit, ct)) throw Missing();
        return JobOperationAuditView.From(audit);
    }

    private async ValueTask<bool> CanInspectAuditAsync(JobApiActor actor, JobOperationAuditSnapshot value, CancellationToken ct)
    {
        if (!await access.CanAsync(actor, JobsPermissions.AuditRead,
            new(actor.Scope.TenantId!, "operation-audit", value.OperationId), ct)) return false;
        // Target authorization is reevaluated on every read. A retained confirmation never grants access.
        if (value.Audit?.Target is not { } target) return true;
        if (target.Origin is { } origin)
        {
            if (!await access.CanAsync(actor, JobsPermissions.AuditRead,
                new(actor.Scope.TenantId!, "job-origin", origin.OriginId) { Origin = origin }, ct)) return false;
            var retained = await queries.FindOriginAsync(actor.Scope, origin, ct);
            if (retained is null) return false;
            if (retained.Acceptance is not { } acceptance) return retained.Cancelled && !retained.Retired;
            var job = await queries.FindAsync(actor.Scope, acceptance.JobId, ct);
            return await access.CanAsync(actor, JobsPermissions.AuditRead, job is null
                ? new(actor.Scope.TenantId!, "job", acceptance.JobId) : Resource(actor, job), ct);
        }
        if (target.Gate is { } gate)
            return await access.CanAsync(actor, JobsPermissions.AuditRead, GateResource(actor, gate), ct);
        if (target.JobId is { } jobId)
        {
            var job = await queries.FindAsync(actor.Scope, jobId, ct);
            return await access.CanAsync(actor, JobsPermissions.AuditRead, job is null
                ? new(actor.Scope.TenantId!, "job", jobId) : Resource(actor, job), ct);
        }
        if (target.ScheduleId is { } scheduleId)
        {
            var schedule = await queries.FindScheduleAsync(actor.Scope, scheduleId, ct);
            return await access.CanAsync(actor, JobsPermissions.AuditRead, schedule is null
                ? new(actor.Scope.TenantId!, "schedule", scheduleId) : ScheduleResource(actor, schedule), ct);
        }
        return false;
    }
}
