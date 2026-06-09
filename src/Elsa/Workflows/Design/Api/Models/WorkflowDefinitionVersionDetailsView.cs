namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionVersionDetailsView(
    string Id,
    string Version,
    WorkflowDefinitionView Definition,
    WorkflowDefinitionStateView State
);
