using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveActivityFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveActivityFromDraftCommand
{
    public Task Execute(string draftId, string nodeId, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            async (draft, dbContext) =>
            {
                // Drop the activity from State.Activities and any connections referencing the NodeId.
                var activities = draft.State.Activities.Where(a => a.NodeId != nodeId);
                var connections = draft.State.ActivityConnections.Where(
                    c => c.Source.ActivityNodeId != nodeId && c.Target.ActivityNodeId != nodeId
                );

                draft.State = draft.State with { Activities = activities, ActivityConnections = connections };

                // Drop the matching layout record from the Draft's layout sibling.
                var layout = await dbContext.WorkflowDefinitionDraftLayouts.FirstOrDefaultAsync(
                    l => l.WorkflowDefinitionDraftId == draftId, cancellationToken
                );

                layout?.Records = [.. layout.Records.Where(r => r.NodeId != nodeId)];

                return new OnActivityRemovedFromDraft(draftId, nodeId);
            }, 
            cancellationToken
        );
    }
}
