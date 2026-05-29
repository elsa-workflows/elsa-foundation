using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveWorkflowOutputFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveWorkflowOutputFromDraftCommand
{
    public Task Execute(string draftId, string outputReferenceKey, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var outputs = draft.State.Outputs.Where(o => o.ReferenceKey != outputReferenceKey);
                draft.State = draft.State with { Outputs = outputs };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnWorkflowOutputRemovedFromDraft(draftId, outputReferenceKey)
                );
            }, 
            cancellationToken
        );
    }
}
