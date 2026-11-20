using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record AnalyzeScopedVariablesRequest(WorkflowDefinitionStateView? State, string? NodeId);

public sealed record ScopedVariableAnalysisResponse(
    IReadOnlyList<VisibleVariableView> VisibleVariables,
    IReadOnlyList<ScopedVariableShadowingWarning> ShadowingWarnings);

public sealed record ActivityInputOptionsRequest
{
    public string ActivityVersionId { get; init; } = null!;
    public string InputName { get; init; } = null!;
    public string? NodeId { get; init; }
    public WorkflowDefinitionStateView? WorkflowState { get; init; }
}

public sealed record ActivityInputOptionsResponse(
    IReadOnlyList<ActivityInputOption>? Options = null,
    string? Error = null,
    string? Code = null);
