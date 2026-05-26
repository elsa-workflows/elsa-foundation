using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa3.Mapping.Models;

public sealed record WorkflowDefinitionVersionImport(string Id, int Version, DateTimeOffset? SourceCreatedAt, WorkflowDefinitionState State, WorkflowDefinitionImport Definition)
    : IWorkflowDefinitionVersion
{
    public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;

    public DateTimeOffset LastModifiedAt => CreatedAt;

    IWorkflowDefinition IWorkflowDefinitionVersion.Definition => Definition;
}