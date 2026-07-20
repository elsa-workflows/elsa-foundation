using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record UpdateDefinitionMetadata(
    string OperationKey,
    string DefinitionId,
    string? Name = null,
    JsonElement? Description = null) : ICommand<WorkflowDefinitionDetailsView>;

public sealed record ReplaceDraft(
    string OperationKey,
    string DraftId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView>? Layout = null) : ICommand<WorkflowDraftView>;

public sealed record PromoteDraft(string OperationKey, string DraftId) : ICommand<WorkflowDefinitionVersionDetailsView>;

public sealed record DiscardDraft(string OperationKey, string DraftId) : ICommand;

public sealed record SoftDeleteDefinition(string OperationKey, string DefinitionId, string? Reason = null) : ICommand;

public sealed record RestoreDefinition(string OperationKey, string DefinitionId) : ICommand;

public sealed record DeleteDefinitionPermanently(string OperationKey, string DefinitionId) : ICommand;
