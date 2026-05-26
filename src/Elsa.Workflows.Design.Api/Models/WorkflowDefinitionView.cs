namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionView(
    string Id,
    string Name,
    string? Description,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt
);
