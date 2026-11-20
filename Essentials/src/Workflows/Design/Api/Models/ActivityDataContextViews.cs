using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>A location in an acknowledged draft, optionally analyzed against its unsaved canonical working snapshot.</summary>
public sealed record ActivityDataContextRequest(
    string ContractVersion,
    string DefinitionId,
    string DraftId,
    string ExpectedBaseRevision,
    string DocumentRevision,
    string NodeId,
    string InputKey,
    WorkflowDefinitionStateView? Snapshot = null,
    string? ExpectedSnapshotHash = null,
    string? PreviousContextRevision = null,
    ActivityDataSelectionQuery? Query = null);

/// <summary>Metadata only. Refused or stale responses carry no analysis or candidate content.</summary>
public sealed record ActivityDataContextResponse(
    string ContractVersion,
    string State,
    string Code,
    string ResourceKind,
    string ResourceId,
    string? BaseRevision,
    string DocumentRevision,
    string? SnapshotHash = null,
    string? ContextRevision = null,
    ActivityDataSelectionAnalysis? Analysis = null);
