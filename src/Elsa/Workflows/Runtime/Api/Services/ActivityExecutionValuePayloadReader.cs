using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

public sealed class ActivityExecutionValuePayloadReader(
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionInspectionStore inspections,
    IActivityInspectionContextAsync authorization,
    IActivityExecutionValuePayloadAuditSink auditSink,
    TimeProvider timeProvider) : IActivityExecutionValuePayloadReader
{
    public async ValueTask<ActivityExecutionValuePayloadReadResult> ReadAsync(
        string workflowExecutionId,
        string activityExecutionId,
        string evidenceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);

        var workflow = await workflowExecutions.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || !await authorization.CanInspectStructureAsync(workflow, cancellationToken))
            return ActivityExecutionValuePayloadReadResult.NotFound();
        var projection = await inspections.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        if (projection is null)
            return ActivityExecutionValuePayloadReadResult.NotFound();

        var snapshots = projection.ValueSnapshots.ToArray();
        var snapshot = snapshots
            .Select((value, index) => new
            {
                Value = value,
                EvidenceId = value.EvidenceId ?? ActivityExecutionValueEvidenceIdentity.Create(activityExecutionId, value, index)
            })
            .FirstOrDefault(x => StringComparer.Ordinal.Equals(x.EvidenceId, evidenceId))
            ?.Value;
        if (snapshot is null)
            return ActivityExecutionValuePayloadReadResult.NotFound();

        var authorizationSnapshot = new PayloadResolutionAuthorizationSnapshot(
            authorization.TenantScope,
            await authorization.GetAuthorizationProfileAsync(cancellationToken),
            authorization.AuditSubject,
            authorization.RequestCorrelationId,
            CanResolve: false);
        if (string.IsNullOrWhiteSpace(authorizationSnapshot.AuditSubject))
            return ActivityExecutionValuePayloadReadResult.Denied();

        authorizationSnapshot = authorizationSnapshot with
        {
            CanResolve = await authorization.CanResolveSensitiveValuePayloadsAsync(workflow, cancellationToken)
        };
        if (!authorizationSnapshot.CanResolve)
        {
            await AuditAsync("denied", workflowExecutionId, activityExecutionId, evidenceId, authorizationSnapshot, cancellationToken);
            return ActivityExecutionValuePayloadReadResult.Denied();
        }
        if (snapshot.Payload is null ||
            snapshot.CaptureMode is not (RuntimePayloadCaptureMode.DiagnosticSnapshot or RuntimePayloadCaptureMode.Payload))
        {
            await AuditAsync("unavailable", workflowExecutionId, activityExecutionId, evidenceId, authorizationSnapshot, cancellationToken);
            return ActivityExecutionValuePayloadReadResult.Unavailable();
        }

        await AuditAsync("resolved", workflowExecutionId, activityExecutionId, evidenceId, authorizationSnapshot, cancellationToken);
        return ActivityExecutionValuePayloadReadResult.Resolved(new(
            evidenceId,
            snapshot.CaptureMode.ToString(),
            snapshot.Payload.Value.Clone()));
    }

    private ValueTask AuditAsync(
        string outcome,
        string workflowExecutionId,
        string activityExecutionId,
        string evidenceId,
        PayloadResolutionAuthorizationSnapshot authorizationSnapshot,
        CancellationToken cancellationToken) =>
        auditSink.RecordAsync(
            new(
                workflowExecutionId,
                activityExecutionId,
                evidenceId,
                authorizationSnapshot.TenantScope,
                authorizationSnapshot.AuthorizationProfile,
                authorizationSnapshot.AuditSubject,
                authorizationSnapshot.RequestCorrelationId,
                outcome,
                timeProvider.GetUtcNow()),
            cancellationToken);

    private sealed record PayloadResolutionAuthorizationSnapshot(
        string TenantScope,
        string AuthorizationProfile,
        string AuditSubject,
        string RequestCorrelationId,
        bool CanResolve);
}
