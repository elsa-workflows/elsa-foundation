using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class UpdateWorkflowOutputInDraftCommand(DraftMutationPipeline pipeline) : IUpdateWorkflowOutputInDraftCommand
{
    public Task Execute(string draftId, string outputReferenceKey, OutputDefinition newValue, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var oldValue = draft.State.Outputs.FirstOrDefault(o => o.ReferenceKey == outputReferenceKey)
                    ?? throw new InvalidOperationException($"Workflow output '{outputReferenceKey}' not found in draft '{draftId}'");

                var outputs = draft.State.Outputs.Select(o => o.ReferenceKey == outputReferenceKey ? newValue : o).ToList();
                draft.State = draft.State with { Outputs = outputs };

                return ValueTask.FromResult<IEvent>(
                    new OnWorkflowOutputUpdatedInDraft(draftId, outputReferenceKey, oldValue, newValue)
                );
            }, 
            cancellationToken
        );
    }
}
