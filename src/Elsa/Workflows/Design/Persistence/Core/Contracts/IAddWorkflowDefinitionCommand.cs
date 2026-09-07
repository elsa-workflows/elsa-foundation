using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Models;

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

    Task<WorkflowDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        IReadOnlyCollection<ActivityPresentationRecord> activityPresentation,
        CancellationToken cancellationToken = default) =>
        activityPresentation.Count == 0
            ? Execute(operationKey, workflowDefinition, draft, layout, cancellationToken)
            : Task.FromException<WorkflowDefinitionCreated>(new NotSupportedException(
                $"The configured {nameof(IAddWorkflowDefinitionCommand)} does not support initial activity presentation persistence."));
}
