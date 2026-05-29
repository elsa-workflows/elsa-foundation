using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class AddConnectionToDraftCommand(DraftMutationPipeline pipeline) : IAddConnectionToDraftCommand
{
    public Task Execute(string draftId, ActivityConnection connection, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var connections = draft.State.ActivityConnections.Append(connection);
                draft.State = draft.State with { ActivityConnections = connections };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnConnectionAddedToDraft(draftId, connection)
                );
            }, 
            cancellationToken
        );
    }
}
