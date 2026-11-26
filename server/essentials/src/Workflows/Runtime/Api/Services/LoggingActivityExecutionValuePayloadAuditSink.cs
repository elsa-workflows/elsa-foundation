using Elsa.Workflows.Runtime.Api.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Services;

public sealed class LoggingActivityExecutionValuePayloadAuditSink(
    ILogger<LoggingActivityExecutionValuePayloadAuditSink> logger) : IActivityExecutionValuePayloadAuditSink
{
    public ValueTask RecordAsync(
        ActivityExecutionValuePayloadAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Activity execution value payload resolution {Outcome} for workflow {WorkflowExecutionId}, activity {ActivityExecutionId}, evidence {EvidenceId}, tenant scope {TenantScope}, authorization profile {AuthorizationProfile}, audit subject {AuditSubject}, request {RequestCorrelationId} at {OccurredAt}.",
            record.Outcome,
            record.WorkflowExecutionId,
            record.ActivityExecutionId,
            record.EvidenceId,
            record.TenantScope,
            record.AuthorizationProfile,
            record.AuditSubject,
            record.RequestCorrelationId,
            record.OccurredAt);
        return ValueTask.CompletedTask;
    }
}
