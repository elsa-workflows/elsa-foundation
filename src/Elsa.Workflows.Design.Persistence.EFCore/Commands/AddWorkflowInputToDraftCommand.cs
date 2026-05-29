using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class AddWorkflowInputToDraftCommand(DraftMutationPipeline pipeline) : IAddWorkflowInputToDraftCommand
{
    public Task Execute(string draftId, InputDefinition input, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var inputs = draft.State.Inputs.Append(input);
                draft.State = draft.State with { Inputs = inputs };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnWorkflowInputAddedToDraft(draftId, input)
                );
            }, 
            cancellationToken
        );
    }
}
