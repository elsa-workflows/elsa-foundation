using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional queue capability that reads work and its visibility deadline in the same bounded provider snapshot.
/// </summary>
/// <remarks>
/// The deadline covers both an active claim and a released claim's retry delay. This is a read, not a claim:
/// consumers must hold the workflow execution's single-writer lease while using it as an overlay baseline.
/// A missing deadline means the item is visible. Pages must follow the queue's actual claim order without
/// hiding delayed items, even when its general-purpose item listing uses a different order. Continuations
/// are scoped to this traversal and need not interoperate with item-only listing continuations.
/// </remarks>
public interface IWorkflowSchedulerWorkVisibilityReader
{
    ValueTask<RuntimeStorePage<RuntimeSchedulerWorkVisibility>> ListWithVisibilityAsync(
        RuntimeSchedulerWorkQuery query,
        CancellationToken cancellationToken = default);
}
