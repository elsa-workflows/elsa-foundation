using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Activities.Design.Api.Models;

/// <summary>Names only an activity-draft location; the server owns its graph, context and symbols.</summary>
public sealed record ActivityExpressionToolingRequest(
    ExpressionToolingContractVersion ContractVersion,
    string ActivityDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string? ContextRevision = null,
    string? Search = null,
    int Skip = 0,
    int Take = 100,
    string Source = "",
    ExpressionToolingPosition Cursor = default,
    ExpressionToolingPosition Position = default)
{
    internal ResolveExpressionAuthoringContextRequest ToLocation() =>
        new(ContractVersion, "", NodeId, PropertyKey, ExpressionType, DocumentRevision, ContextRevision, Search, Skip, Take)
        { Owner = new("activity-draft", ActivityDraftId) };
}

public sealed record ActivityExpressionToolingResponse<T>(ExpressionToolingOutcome<T> Result);

public sealed record ActivityExpressionToolingDescriptor(
    string ExpressionType,
    string ModuleId,
    string ModuleVersion,
    ExpressionToolingContractVersion ContractVersion,
    ExpressionToolingCapabilities Capabilities);
