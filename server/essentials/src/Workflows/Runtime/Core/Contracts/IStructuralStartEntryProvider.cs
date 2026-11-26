using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Names, for one structural activity kind, the child that a run started by a given trigger begins at. The trigger
/// binding preflight walks the path from the root activity to every node that produces start bindings and asks the
/// provider for each composite on that path. A start trigger is valid only when every composite begins at the next
/// node of the path (spec 238, the start-path rule). A composite whose structure kind has no provider refuses.
/// </summary>
/// <remarks>
/// Activity modules implement this for the structures they own. The runtime keeps no knowledge of activity-specific
/// structure (spec 070). Register implementations with <c>TryAddEnumerable</c>. Two providers for one
/// <see cref="StructureKind"/> fail the preflight as a host configuration error.
/// </remarks>
public interface IStructuralStartEntryProvider
{
    /// <summary>The <see cref="ExecutableActivityStructure.Kind"/> this provider answers for.</summary>
    string StructureKind { get; }

    /// <summary>
    /// The direct child of <paramref name="composite"/> that a run started by <paramref name="triggerNodeId"/>
    /// legitimately begins at, or <c>null</c> when no run started by that trigger can begin inside this composite.
    /// </summary>
    /// <param name="composite">A structural node whose structure kind is <see cref="StructureKind"/>.</param>
    /// <param name="triggerNodeId">The start trigger's executable node id. It may be the composite's direct child or a deeper descendant.</param>
    ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId);
}
