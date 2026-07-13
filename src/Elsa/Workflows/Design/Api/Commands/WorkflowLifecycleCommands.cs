using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record UpdateDefinitionMetadata(
    string DefinitionId,
    string? Name = null,
    JsonElement? Description = null) : ICommand<WorkflowDefinitionDetailsView>;

public sealed record ReplaceDraft(
    string DraftId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView>? Layout = null) : ICommand<WorkflowDraftView>;

public sealed record PromoteDraft(string DraftId) : ICommand<WorkflowDefinitionVersionDetailsView>;

public sealed record DiscardDraft(string DraftId) : ICommand;

public sealed record SoftDeleteDefinition(string DefinitionId, string? Reason = null) : ICommand;

public sealed record RestoreDefinition(string DefinitionId) : ICommand;

public sealed record DeleteDefinitionPermanently(string DefinitionId) : ICommand;
