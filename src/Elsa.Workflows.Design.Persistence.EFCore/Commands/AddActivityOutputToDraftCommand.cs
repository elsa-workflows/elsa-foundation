using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class AddActivityOutputToDraftCommand(DraftMutationPipeline pipeline) : IAddActivityOutputToDraftCommand
{
    public Task Execute(string draftId, string nodeId, ArgumentState output, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                draft.State = draft.State.WithMutatedActivity(
                    nodeId, 
                    node => node with { Outputs = [.. node.Outputs, output] }
                );

                return ValueTask.FromResult<IEvent>(
                    new OnActivityOutputAddedToDraft(draftId, nodeId, output)
                );
            }, 
            cancellationToken
        );
    }
}
