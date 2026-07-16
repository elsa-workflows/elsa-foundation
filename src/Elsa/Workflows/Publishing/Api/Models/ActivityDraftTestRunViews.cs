using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record ActivityDraftTestRunInput(string State, JsonElement? Value = null);

public sealed record StartActivityDraftTestRun(
    [property: JsonIgnore] string DraftId,
    long ExpectedRevision,
    IReadOnlyDictionary<string, ActivityDraftTestRunInput>? Inputs = null,
    string? CorrelationId = null) : IRequest<ActivityDraftTestRunView>;

public sealed record ActivityDraftTestRunView(
    string TestRunId,
    string DraftId,
    long DraftRevision,
    string ArtifactId,
    string SourceReferenceId,
    string WorkflowExecutionId,
    string? OuterActivityExecutionId,
    string Status,
    string CommandDispatchStatus,
    string? Reason,
    DateTimeOffset ExpiresAt);
