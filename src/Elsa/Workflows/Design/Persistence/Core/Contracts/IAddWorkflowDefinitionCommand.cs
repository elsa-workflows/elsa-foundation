using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface IAddWorkflowDefinitionCommand
{
    Task<WorkflowDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        CancellationToken cancellationToken = default) =>
        layout.Count == 0
            ? Execute(operationKey, workflowDefinition, draft, cancellationToken)
            : Task.FromException<WorkflowDefinitionCreated>(new NotSupportedException(
                $"The configured {nameof(IAddWorkflowDefinitionCommand)} does not support initial draft layout persistence."));
}
