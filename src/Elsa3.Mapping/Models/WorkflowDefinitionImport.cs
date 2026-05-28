using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa3.Mapping.Models;

public sealed record WorkflowDefinitionImport(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    IWorkflowDefinitionDraft? Draft
)
    : IWorkflowDefinition
{


    public IWorkflowDefinition ShallowClone() => (WorkflowDefinitionImport)MemberwiseClone();
}
