namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionVersionView(
    string Id,
    int Version,
    DateTimeOffset CreatedAt
);
