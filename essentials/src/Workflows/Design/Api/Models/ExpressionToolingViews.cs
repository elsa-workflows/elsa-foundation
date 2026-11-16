using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>Wire request deliberately names only the location; context and symbols remain server-authoritative.</summary>
public sealed record ExpressionToolingContextRequest(
    ExpressionToolingContractVersion ContractVersion,
    string WorkflowDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string? ContextRevision = null,
    string? Search = null,
    int Skip = 0,
    int Take = 100);

public sealed record ExpressionToolingContextResponse(ExpressionToolingOutcome<ExpressionAuthoringContext> Result);

public sealed record ExpressionToolingDescriptor(
    string ExpressionType,
    string ModuleId,
    string ModuleVersion,
    ExpressionToolingContractVersion ContractVersion,
    ExpressionToolingCapabilities Capabilities);

public sealed record ExpressionToolingDescriptorsResponse(
    ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>> Result);

public sealed record ExpressionToolingSourceRequest(
    ExpressionToolingContractVersion ContractVersion,
    string WorkflowDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string Source,
    string? ContextRevision = null);

public sealed record ExpressionToolingCompletionRequest(
    ExpressionToolingContractVersion ContractVersion,
    string WorkflowDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string Source,
    ExpressionToolingPosition Cursor,
    string? ContextRevision = null);

public sealed record ExpressionToolingHoverRequest(
    ExpressionToolingContractVersion ContractVersion,
    string WorkflowDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string Source,
    ExpressionToolingPosition Position,
    string? ContextRevision = null);

public sealed record ExpressionToolingOperationResponse<T>(ExpressionToolingOutcome<T> Result);
