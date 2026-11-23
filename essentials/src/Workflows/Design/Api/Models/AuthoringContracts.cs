using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record AnalyzeScopedVariablesRequest(WorkflowDefinitionStateView? State, string? NodeId);

/// <param name="ScriptWrites">
/// For a script activity whose body is authored as a literal, the variables the body may write (spec 1231 FR-036);
/// otherwise null.
/// </param>
public sealed record ScopedVariableAnalysisResponse(
    IReadOnlyList<VisibleVariableView> VisibleVariables,
    IReadOnlyList<ScopedVariableShadowingWarning> ShadowingWarnings,
    ScriptBodyWriteSet? ScriptWrites = null);

/// <summary>Asks how a draft's scripts follow the rename of one variable (spec 1222 FR-022).</summary>
public sealed record PlanVariableRenameRequest(WorkflowDefinitionStateView? State, string? ScopeId, string? ReferenceKey, string? NewName);

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
