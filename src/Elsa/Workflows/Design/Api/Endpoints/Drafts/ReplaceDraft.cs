using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

public sealed record ReplaceDraft(
    string? OperationKey,
    string DraftId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView>? Layout = null,
    IReadOnlyCollection<ActivityPresentationRecordView>? ActivityPresentation = null) : ICommand<WorkflowDraftView>;
