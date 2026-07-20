using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

/// <summary>
/// The HTTP-surface command behind <c>PUT design/workflows/definitions/{id}</c> — the DS-6 Draft mutation
/// gate. It carries the complete desired Draft <see cref="State"/> (and optional <see cref="Layout"/>) for the
/// definition's current Draft; the handler resolves the owning Draft and forwards to the single coarse
/// <c>IUpdateDraftCommand</c>, which diffs the desired state against stored state and emits the per-concept
/// mutation events. There is no partial/patch mode (full-state-always, FR-001).
/// </summary>
public sealed record UpdateDefinition(
    string OperationKey,
    string Id,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView>? Layout = null
) : ICommand<WorkflowDefinitionDetailsView>;
