using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record ActivityDraftTestRunInput(string State, JsonElement? Value = null);

public sealed record StartActivityDraftTestRun(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    string IdempotencyKey,
    IReadOnlyDictionary<string, ActivityDraftTestRunInput>? Inputs = null,
    string? CorrelationId = null) : IRequest<ActivityDraftTestRunView>;

public sealed record ActivityDraftTestRunView(
    string TestRunId,
    string DraftId,
    long DraftRevision,
    string? ArtifactId,
    string? SourceReferenceId,
    string WorkflowExecutionId,
    string? OuterActivityExecutionId,
    string Status,
    string? CommandDispatchStatus,
    string? Reason,
    ActivityDraftTestRunFailureView? Failure,
    ActivityDraftTestRunExpirationView Expiration,
    ActivityDraftTestRunCancellationView Cancellation,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt);

public sealed record ActivityDraftTestRunFailureView(
    string Kind,
    string Code,
    string Message,
    IReadOnlyList<Elsa.Activities.Design.Core.Models.ActivityDiagnostic> Diagnostics);

public sealed record ActivityDraftTestRunExpirationView(
    DateTimeOffset SourceReferenceExpiresAt,
    bool SourceReferenceExpired,
    bool SourceReferenceRetained,
    string EvidenceRetention,
    DateTimeOffset? EvidenceExpiresAt,
    bool EvidenceRetained,
    DateTimeOffset? RunExpiresAt,
    bool RunStillActive,
    DateTimeOffset ReceiptExpiresAt);

public sealed record ActivityDraftTestRunCancellationView(
    bool CapabilityAdvertised,
    bool Available,
    string Status,
    string? Reason);

public sealed record GetActivityDraftTestRun(
    [property: JsonIgnore] string TestRunId) : IRequest<ActivityDraftTestRunView>;

public sealed record GetActivityDraftTestRunByIdempotencyKey(
    [property: JsonIgnore] string DraftId,
    [property: JsonIgnore] string IdempotencyKey) : IRequest<ActivityDraftTestRunView>;

public sealed record CancelActivityDraftTestRun(
    [property: JsonIgnore] string TestRunId) : IRequest<ActivityDraftTestRunView>;
