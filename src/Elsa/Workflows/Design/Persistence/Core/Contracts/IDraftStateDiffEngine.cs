using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

public interface IDraftStateDiffEngine
{
    IReadOnlyList<IEvent> Evaluate(
        string draftId,
        WorkflowDefinitionState stored,
        IReadOnlyCollection<DesignMetadataRecord> storedLayout,
        WorkflowDefinitionState desired,
        IReadOnlyCollection<DesignMetadataRecord> desiredLayout);
}
