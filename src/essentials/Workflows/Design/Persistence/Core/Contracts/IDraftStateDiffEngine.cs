using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Computes the per-diff mutation events (the <c>On*InDraft</c>/<c>On*ToDraft</c> records) between a
/// stored and a desired Draft <c>State</c> + layout.
/// </summary>
/// <remarks>
/// Currently NOT invoked by any mutation command and NOT registered in DI: per-diff mutation-event
/// publication is retired until a consumer (the FR-017/FR-018 event-sourcing slot) exists — the events
/// had no subscriber. This engine and the event records it produces remain in place as the tested
/// contract to re-wire at that point.
/// </remarks>
public interface IDraftStateDiffEngine
{
    IReadOnlyList<IEvent> Evaluate(
        string draftId,
        WorkflowDefinitionState stored,
        IReadOnlyCollection<DesignMetadataRecord> storedLayout,
        WorkflowDefinitionState desired,
        IReadOnlyCollection<DesignMetadataRecord> desiredLayout);
}
