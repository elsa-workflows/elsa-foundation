using Elsa.Expressions.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class DeclareVariableInDraftCommand(DraftMutationPipeline pipeline) : IDeclareVariableInDraftCommand
{
    public Task Execute(string draftId, VariableDefinition variable, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var variables = draft.State.Variables.Append(variable);
                draft.State = draft.State with { Variables = variables };

                return ValueTask.FromResult<ILifecycleEvent>(
                    new OnVariableDeclaredInDraft(draftId, variable)
                );
            }, 
            cancellationToken
        );
    }
}
