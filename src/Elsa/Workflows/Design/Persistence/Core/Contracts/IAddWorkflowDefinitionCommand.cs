using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface IAddWorkflowDefinitionCommand
{
    Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellation);

    Task Execute(
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        CancellationToken cancellation) =>
        layout.Count == 0
            ? Execute(workflowDefinition, draft, cancellation)
            : Task.FromException(new NotSupportedException(
                $"The configured {nameof(IAddWorkflowDefinitionCommand)} does not support initial draft layout persistence."));
}
