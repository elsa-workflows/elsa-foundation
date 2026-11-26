namespace Elsa.Workflows.Runtime.Api.Contracts;

public interface IActivityExecutionValuePayloadAuditSink
{
    ValueTask RecordAsync(ActivityExecutionValuePayloadAuditRecord record, CancellationToken cancellationToken = default);
}

public sealed record ActivityExecutionValuePayloadAuditRecord(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string EvidenceId,
    string TenantScope,
    string AuthorizationProfile,
    string AuditSubject,
    string RequestCorrelationId,
    string Outcome,
    DateTimeOffset OccurredAt);
