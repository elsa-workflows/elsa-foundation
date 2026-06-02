using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveVariableFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveVariableFromDraftCommand
{
    public Task Execute(string draftId, string variableReferenceKey, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var variables = draft.State.Variables.Where(v => v.ReferenceKey != variableReferenceKey);
                draft.State = draft.State with { Variables = variables };

                return ValueTask.FromResult<IEvent>(
                    new OnVariableRemovedFromDraft(draftId, variableReferenceKey)
                );
            }, 
            cancellationToken
        );
    }
}
