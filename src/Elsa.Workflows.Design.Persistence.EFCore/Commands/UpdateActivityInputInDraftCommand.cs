using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class UpdateActivityInputInDraftCommand(DraftMutationPipeline pipeline) : IUpdateActivityInputInDraftCommand
{
    public Task Execute(
        string draftId,
        string nodeId,
        string inputReferenceKey,
        ArgumentState newValue,
        CancellationToken cancellationToken = default
    )
    {
        return pipeline.ExecuteMutation(
            draftId,
            (draft, _) => Execute(draft, nodeId, inputReferenceKey, newValue),
            cancellationToken
        );
    }

    private static ValueTask<ILifecycleEvent> Execute(        
        WorkflowDefinitionDraft draft, 
        string nodeId,
        string inputReferenceKey,
        ArgumentState newValue
    )
    {
        ArgumentState? oldValue = null;

        draft.State = draft.State.WithMutatedActivity(
            nodeId,
            node =>
            {
                oldValue = node.Inputs.FirstOrDefault(i => i.ReferenceKey == inputReferenceKey);

                var inputs = node.Inputs
                    .Select(i => i.ReferenceKey == inputReferenceKey ? newValue : i)
                    .ToArray();

                return node with { Inputs = inputs };
            }
        );

        if (oldValue is null)
        {
            throw new InvalidOperationException(
                $"Input '{inputReferenceKey}' not found on activity '{nodeId}' in draft '{draft.Id}'"
            );
        }

        var lifecycleEvent = new OnActivityInputUpdatedInDraft(
            draft.Id,
            nodeId,
            inputReferenceKey,
            oldValue,
            newValue
        );

        return ValueTask.FromResult<ILifecycleEvent>(lifecycleEvent);
    }
}
