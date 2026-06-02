using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveConnectionFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveConnectionFromDraftCommand
{
    public Task Execute(string draftId, ActivityConnection connection, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                // Identity is the source+target pair; equality via record value semantics.
                var connections = draft.State.ActivityConnections.Where(c => c != connection);
                draft.State = draft.State with { ActivityConnections = connections };

                return ValueTask.FromResult<IEvent>(
                    new OnConnectionRemovedFromDraft(draftId, connection)
                );
            }, 
            cancellationToken
        );
    }
}
