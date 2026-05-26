namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionVersionDetailsView(
    string Id,
    int Version,
    WorkflowDefinitionView Definition,
    WorkflowDefinitionStateView State
);
