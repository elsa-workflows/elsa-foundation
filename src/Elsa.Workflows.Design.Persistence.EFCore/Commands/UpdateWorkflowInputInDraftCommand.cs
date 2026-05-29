using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class UpdateWorkflowInputInDraftCommand(DraftMutationPipeline pipeline) : IUpdateWorkflowInputInDraftCommand
{
    public Task Execute(string draftId, string inputReferenceKey, InputDefinition newValue, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var oldValue = draft.State.Inputs.FirstOrDefault(i => i.ReferenceKey == inputReferenceKey)
                    ?? throw new InvalidOperationException($"Workflow input '{inputReferenceKey}' not found in draft '{draftId}'");

                var inputs = draft.State.Inputs.Select(i => i.ReferenceKey == inputReferenceKey ? newValue : i);
                draft.State = draft.State with { Inputs = inputs };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnWorkflowInputUpdatedInDraft(draftId, inputReferenceKey, oldValue, newValue)
                );
            }, 
            cancellationToken
        );
    }
}
