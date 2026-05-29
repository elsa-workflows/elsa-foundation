using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class UpdateActivityOutputInDraftCommand(DraftMutationPipeline pipeline) : IUpdateActivityOutputInDraftCommand
{
    public Task Execute(
        string draftId,
        string nodeId,
        string outputReferenceKey,
        ArgumentState newValue,
        CancellationToken cancellationToken = default
    )
    {
        return pipeline.ExecuteMutation(
            draftId,
            (draft, _) => Execute(draft, nodeId, outputReferenceKey, newValue),
            cancellationToken
        );
    }

    private static ValueTask<ILifecycleEvent> Execute(
        WorkflowDefinitionDraft draft,
        string nodeId,
        string outputReferenceKey,
        ArgumentState newValue
   )
    {
        ArgumentState? oldValue = null;

        draft.State = draft.State.WithMutatedActivity(
            nodeId,
            node =>
            {
                oldValue = node.Outputs.FirstOrDefault(o => o.ReferenceKey == outputReferenceKey);

                var outputs = node.Outputs
                    .Select(o => o.ReferenceKey == outputReferenceKey ? newValue : o)
                    .ToArray();

                return node with { Outputs = outputs };
            }
        );

        if (oldValue is null)
        {
            throw new InvalidOperationException(
                $"Output '{outputReferenceKey}' not found on activity '{nodeId}' in draft '{draft.Id}'"
            );
        }

        var lifecycleEvent = new OnActivityOutputUpdatedInDraft(
            draft.Id,
            nodeId,
            outputReferenceKey,
            oldValue,
            newValue
        );

        return ValueTask.FromResult<ILifecycleEvent>(lifecycleEvent);
    }
}
