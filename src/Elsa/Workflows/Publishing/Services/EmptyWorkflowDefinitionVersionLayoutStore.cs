using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Fallback <see cref="IWorkflowDefinitionVersionLayoutStore"/> for compositions with no design-persistence
/// provider (e.g. in-memory publishing). It reports no stored layout, so a publish still succeeds and simply
/// embeds an empty layout sidecar. A real persistence provider overrides this with its own registration.
/// </summary>
public sealed class EmptyWorkflowDefinitionVersionLayoutStore : IWorkflowDefinitionVersionLayoutStore
{
    public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
}
