using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa3.Mapping.Models;

public sealed record WorkflowDefinitionImport(
    string Id,
    string Name,
    string? Description,
    WorkflowMetadata? MetaData,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    IWorkflowDefinitionDraft? Draft
)
    : IWorkflowDefinition
{


    public IWorkflowDefinition ShallowClone() => (WorkflowDefinitionImport)MemberwiseClone();
}