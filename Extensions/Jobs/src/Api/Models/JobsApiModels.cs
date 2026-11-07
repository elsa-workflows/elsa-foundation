using System.Text.Json.Serialization;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api.Models;

internal sealed record JobPageQuery(int Limit = 25, string? After = null);
internal sealed record JobListRequest(int Limit = 25, string? After = null, string? Queue = null, string? JobType = null, JobState? State = null);
internal sealed record JobHoldQuery(int Limit = 25, string? After = null, JobGateKind? Kind = null, string? Name = null, string? ModelKind = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record JobInvocationRequest(JobHandlerIdentity Handler, JobPayload Input, JobSchema ResultSchema,
    string Queue, JobExecutionPolicy Policy, DateTimeOffset? NotBefore = null)
{
    internal JobRequest ToRequest(JobAssociation? association = null)
    {
        ArgumentNullException.ThrowIfNull(Handler); ArgumentNullException.ThrowIfNull(Input);
        ArgumentNullException.ThrowIfNull(Input.Schema); ArgumentNullException.ThrowIfNull(ResultSchema);
        ArgumentNullException.ThrowIfNull(Policy); ArgumentNullException.ThrowIfNull(Policy.Retry);
        if (Input.Value.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new ArgumentException("An input JSON value is required.");
        return new(Handler, Input, ResultSchema, Queue, Policy, NotBefore, association);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SubmitJobRequest(string IdempotencyKey, JobInvocationRequest Invocation);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record JobOperationRequest(string OperationId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RetryJobRequest(long ExpectedRevision, string IdempotencyKey, string OperationId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PauseJobsRequest(JobGateKey Gate, string HoldId, string OperationId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReleaseJobHoldRequest(JobGateKey Gate, long ExpectedRevision, string OperationId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SetJobCapacityRequest(JobGateKey Gate, int? Limit, long ExpectedRevision, string OperationId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SaveJobScheduleRequest(JobInvocationRequest Invocation, JobRecurrence Recurrence, DateTimeOffset FirstOccurrence,
    JobMissedOccurrencePolicy MissedPolicy, long ExpectedRevision, string OperationId, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record EnableJobScheduleRequest(bool Enabled, long ExpectedRevision, string OperationId, string Reason);

internal sealed record JobApiPage<T>(IReadOnlyList<T> Items, string? Next);
internal sealed record JobAssociationView(string ModelKind, string DefinitionId, string DefinitionVersion, string ExecutionId,
    string InvocationId, string MaterialId, string MaterialSha256, JobAssociationMode Mode)
{
    internal static JobAssociationView? From(JobAssociation? value) => value is null ? null :
        new(value.ModelKind, value.DefinitionId, value.DefinitionVersion, value.ExecutionId, value.InvocationId,
            value.MaterialId, value.MaterialSha256, value.Mode);
}

internal sealed record JobPermissionView(string AttemptId, int Number, string Owner, string ImplementationIdentity,
    DateTimeOffset GrantedAt, DateTimeOffset ExpiresAt, DateTimeOffset Deadline)
{
    internal static JobPermissionView? From(JobAttemptLease? value) => value is null ? null :
        new(value.AttemptId, value.AttemptNumber, value.Owner, value.ImplementationIdentity, value.GrantedAt, value.ExpiresAt, value.Deadline);
}

internal sealed record JobView(string JobId, JobHandlerIdentity? Handler, JobSchema? InputSchema, JobSchema? ResultSchema,
    string? Queue, JobState State, long Revision, DateTimeOffset AcceptedAt, DateTimeOffset EligibleAt,
    JobPermissionView? ActivePermission, JobOutcome? Outcome, string? ResultId, DateTimeOffset? CompletedAt,
    double? ProgressFraction, bool CancellationRequested, string? RetriedFromJobId, JobAssociationView? Association,
    string? Diagnostic, string ConsumptionState)
{
    public JobPayloadState InputPayloadState { get; init; }
    public JobPayloadState ResultPayloadState { get; init; }
    internal static JobView From(JobSnapshot value) => new(value.JobId, value.RequestMetadata?.Handler ?? value.Request?.Handler, value.RequestMetadata?.InputSchema ?? value.Request?.Input.Schema,
        value.RequestMetadata?.ResultSchema ?? value.Request?.ResultSchema, value.RequestMetadata?.Queue ?? value.Request?.Queue, value.State, value.Revision, value.AcceptedAt, value.EligibleAt,
        JobPermissionView.From(value.ActiveAttempt), value.ResultMetadata?.Outcome ?? value.Result?.Outcome, value.ResultMetadata?.ResultId ?? value.Result?.ResultId,
        value.ResultMetadata?.CompletedAt ?? value.Result?.CompletedAt,
        value.Progress?.Fraction, value.CancellationRequested, value.RetriedFromJobId, JobAssociationView.From(value.RequestMetadata?.Association ?? value.Request?.Association),
        SafeDiagnostic(value.ResultMetadata?.Outcome ?? value.Result?.Outcome, value.UnavailableDiagnostic is not null),
        value.UnavailableDiagnostic is null && (value.RequestMetadata is not null || value.Request is not null) &&
        (value.RequestMetadata?.Association ?? value.Request?.Association) is null or { Mode: JobAssociationMode.Independent or JobAssociationMode.Detached, Continuation: null }
            ? "not-applicable" : "unknown")
        { InputPayloadState = value.InputPayloadState, ResultPayloadState = value.ResultPayloadState };

    internal static string? SafeDiagnostic(JobOutcome? outcome, bool unavailable = false) => unavailable ? "jobs.record.unavailable" : outcome switch
    {
        JobOutcome.Failed => "jobs.result.failed", JobOutcome.TimedOut => "jobs.result.timed-out",
        JobOutcome.Cancelled => "jobs.result.cancelled", _ => null
    };
}

internal sealed record JobAttemptView(JobPermissionView Permission, JobAttemptState State, DateTimeOffset? PhysicalStartedAt,
    DateTimeOffset? PhysicalExitedAt)
{
    internal static JobAttemptView From(JobAttemptSnapshot value) =>
        new(JobPermissionView.From(value.Lease)!, value.State, value.PhysicalStartedAt, value.PhysicalExitedAt);
}
internal sealed record JobPhysicalReceiptView(string ReceiptId, string AttemptId, string Owner, long Fence,
    JobAttemptOutcome Outcome, bool Retryable, DateTimeOffset ReportedAt, JobPayloadState PayloadState, DateTimeOffset? ExpiredAt)
{
    internal static JobPhysicalReceiptView From(JobPhysicalReceiptInspection value) =>
        new(value.Metadata.ReceiptId, value.Metadata.AttemptId, value.Metadata.Owner, value.Metadata.Fence, value.Metadata.Outcome,
            value.Metadata.Retryable, value.Metadata.ReportedAt, value.PayloadState, value.ExpiredAt);
}
internal sealed record JobInputView(string State, JobPayload? Input, string? ValueJson = null,
    JobSchema? Schema = null, string? PayloadSha256 = null, DateTimeOffset? ExpiredAt = null);
internal sealed record JobResultView(string State, string? ResultId, JobOutcome? Outcome, JobPayload? Payload, DateTimeOffset? CompletedAt, string? ValueJson = null,
    JobSchema? Schema = null, string? PayloadSha256 = null, DateTimeOffset? ExpiredAt = null);
internal sealed record JobScheduleView(string ScheduleId, long Revision, bool Enabled, DateTimeOffset NextOccurrence,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, JobHandlerIdentity? Handler, JobSchema? InputSchema, JobSchema? ResultSchema,
    string? Queue, JobExecutionPolicy? Policy, DateTimeOffset? NotBefore, JobRecurrence? Recurrence,
    DateTimeOffset? FirstOccurrence, JobMissedOccurrencePolicy? MissedPolicy, JobAssociationView? Association, string? Diagnostic)
{
    internal static JobScheduleView From(JobScheduleSnapshot value) => new(value.ScheduleId, value.Revision, value.Enabled,
        value.NextOccurrence, value.CreatedAt, value.UpdatedAt, value.Definition?.Template.Handler,
        value.Definition?.Template.Input.Schema, value.Definition?.Template.ResultSchema, value.Definition?.Template.Queue,
        value.Definition?.Template.Policy, value.Definition?.Template.NotBefore, value.Definition?.Recurrence,
        value.Definition?.FirstOccurrence, value.Definition?.MissedPolicy, JobAssociationView.From(value.Definition?.Template.Association),
        value.UnavailableDiagnostic is null ? null : "jobs.schedule.unavailable");
}
internal sealed record JobGateHoldsView(JobGateKey Gate, IReadOnlyList<JobHold> Items, string? Next);
internal sealed record JobHoldsView(IReadOnlyList<JobGateHoldsView> Gates, bool Complete);
internal sealed record JobApiAccessView(string TenantId, string SubjectId, IReadOnlyList<string> Permissions);
internal sealed record JobsApiProblem(string Code, string Message, string? Outcome = null);

internal sealed record JobOperationAuditView(string OperationId, JobOperationKind? Kind, string? Actor,
    string? Reason, DateTimeOffset? RecordedAt, JobOperationTarget? Target, JobTransition? Confirmation, string? Diagnostic)
{
    internal static JobOperationAuditView From(JobOperationAuditSnapshot value) => value.Audit is { } audit
        ? new(value.OperationId, audit.Kind, audit.Operation.Actor, audit.Operation.Reason, audit.RecordedAt,
            audit.Target, audit.Confirmation, null)
        : new(value.OperationId, null, null, null, null, null, null, "jobs.audit.unavailable");
}
