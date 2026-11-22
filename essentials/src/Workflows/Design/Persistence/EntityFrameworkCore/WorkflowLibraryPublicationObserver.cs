using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

/// <summary>Maintains the bounded library membership projection after serving authority changes.</summary>
internal sealed class WorkflowLibraryPublicationObserver(EfWorkflowLibraryStore library,
    IWorkflowActivationAuthority? authority = null) : IWorkflowTriggerIndexObserver
{
    public async ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (authority is null || !snapshot.RequiresProjectionRefresh || snapshot.DefinitionId is not { } definitionId)
            return;
        await library.RefreshPublicationAsync(definitionId, authority, cancellationToken);
    }
}
