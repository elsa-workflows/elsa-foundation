using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>
/// Reads the aggregate authored-lifecycle facts used by workflow-definition list projections.
/// Providers must resolve every supplied definition in a bounded batch operation rather than
/// issuing a persistence call per definition.
/// </summary>
public interface IWorkflowDefinitionListProjectionStore
{
    /// <summary>
    /// Lists projection facts for the supplied workflow definitions. Definitions with neither a
    /// draft nor a version are included with empty lifecycle facts.
    /// </summary>
    Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default);
}
