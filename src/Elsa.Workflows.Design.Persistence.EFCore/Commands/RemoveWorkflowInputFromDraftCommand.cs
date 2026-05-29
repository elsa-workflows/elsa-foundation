using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveWorkflowInputFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveWorkflowInputFromDraftCommand
{
    public Task Execute(string draftId, string inputReferenceKey, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var inputs = draft.State.Inputs.Where(i => i.ReferenceKey != inputReferenceKey);
                draft.State = draft.State with { Inputs = inputs };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnWorkflowInputRemovedFromDraft(draftId, inputReferenceKey)
                );
            }, 
            cancellationToken
        );
    }
}
