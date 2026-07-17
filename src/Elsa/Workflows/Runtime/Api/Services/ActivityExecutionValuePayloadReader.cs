using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

public sealed class ActivityExecutionValuePayloadReader(
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionInspectionStore inspections,
    IActivityExecutionInspectionAuthorizationContext authorization,
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
        if (workflow is null || !authorization.CanInspectStructure(workflow))
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

        if (string.IsNullOrWhiteSpace(authorization.AuditSubject))
            return ActivityExecutionValuePayloadReadResult.Denied();

        if (!authorization.CanResolveSensitiveValuePayloads(workflow))
        {
            await AuditAsync("denied", workflowExecutionId, activityExecutionId, evidenceId, cancellationToken);
            return ActivityExecutionValuePayloadReadResult.Denied();
        }
        if (snapshot.Payload is null ||
            snapshot.CaptureMode is not (RuntimePayloadCaptureMode.DiagnosticSnapshot or RuntimePayloadCaptureMode.Payload))
        {
            await AuditAsync("unavailable", workflowExecutionId, activityExecutionId, evidenceId, cancellationToken);
            return ActivityExecutionValuePayloadReadResult.Unavailable();
        }

        await AuditAsync("resolved", workflowExecutionId, activityExecutionId, evidenceId, cancellationToken);
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
        CancellationToken cancellationToken) =>
        auditSink.RecordAsync(
            new(
                workflowExecutionId,
                activityExecutionId,
                evidenceId,
                authorization.TenantScope,
                authorization.AuthorizationProfile,
                authorization.AuditSubject,
                authorization.RequestCorrelationId,
                outcome,
                timeProvider.GetUtcNow()),
            cancellationToken);
}
