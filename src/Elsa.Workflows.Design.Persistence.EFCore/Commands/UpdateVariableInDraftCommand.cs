using Elsa.Expressions.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class UpdateVariableInDraftCommand(DraftMutationPipeline pipeline) : IUpdateVariableInDraftCommand
{
    public Task Execute(string draftId, string variableReferenceKey, VariableDefinition newValue, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var oldValue = draft.State.Variables.FirstOrDefault(v => v.ReferenceKey == variableReferenceKey)
                    ?? throw new InvalidOperationException($"Variable '{variableReferenceKey}' not found in draft '{draftId}'");

                var variables = draft.State.Variables.Select(v => v.ReferenceKey == variableReferenceKey ? newValue : v);
                draft.State = draft.State with { Variables = variables };

                return ValueTask.FromResult<IEvent>(
                    new OnVariableUpdatedInDraft(draftId, variableReferenceKey, oldValue, newValue)
                );
            }, 
            cancellationToken
        );
    }
}
