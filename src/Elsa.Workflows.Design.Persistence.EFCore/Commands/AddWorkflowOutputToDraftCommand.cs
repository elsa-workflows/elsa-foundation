using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class AddWorkflowOutputToDraftCommand(DraftMutationPipeline pipeline) : IAddWorkflowOutputToDraftCommand
{
    public Task Execute(string draftId, OutputDefinition output, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var outputs = draft.State.Outputs.Append(output);
                draft.State = draft.State with { Outputs = outputs };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnWorkflowOutputAddedToDraft(draftId, output)
                );
            }, 
            cancellationToken
        );
    }
}
