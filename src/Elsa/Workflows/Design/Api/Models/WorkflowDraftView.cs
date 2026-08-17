using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDraftView(
    string Id,
    string DefinitionId,
    string? SourceVersionId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView> Layout,
    IReadOnlyCollection<ActivityPresentationRecordView> ActivityPresentation);
