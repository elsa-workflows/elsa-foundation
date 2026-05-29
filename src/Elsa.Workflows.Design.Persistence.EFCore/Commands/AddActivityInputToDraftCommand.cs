using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class AddActivityInputToDraftCommand(DraftMutationPipeline pipeline) : IAddActivityInputToDraftCommand
{
    public Task Execute(string draftId, string nodeId, ArgumentState input, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                draft.State = draft.State.WithMutatedActivity(
                    nodeId, 
                    node => node with { Inputs = [.. node.Inputs, input] }
                );

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnActivityInputAddedToDraft(draftId, nodeId, input)
                );
            }, 
            cancellationToken
        );
    }

    
}
