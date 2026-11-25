using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// The trigger extractor of a kernel host: it reports no old-core triggers, so the publication preflight checks only the
/// slot and its policy. The kernel's own routes and schedules are checked by <see cref="KernelActivationCoordinator"/>
/// before the slot moves. A test run reads the same extractor and so sees no old-core triggers either.
/// </summary>
public sealed class KernelTriggerBindingExtractor : IWorkflowTriggerBindingExtractor
{
    public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return [];
    }
}
