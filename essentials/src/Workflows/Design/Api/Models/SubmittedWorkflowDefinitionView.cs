namespace Elsa.Workflows.Design.Api.Models;

public sealed record SubmittedWorkflowDefinitionView(
    WorkflowDefinitionView Definition,
    string DraftId,
    WorkflowDefinitionVersionDetailsView Version);
