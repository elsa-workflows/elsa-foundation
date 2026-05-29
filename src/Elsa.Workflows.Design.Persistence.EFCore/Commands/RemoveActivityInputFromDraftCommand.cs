using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveActivityInputFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveActivityInputFromDraftCommand
{
    public Task Execute(
        string draftId,
        string nodeId,
        string inputReferenceKey,
        CancellationToken cancellationToken = default
    )
    {
        return pipeline.ExecuteMutation(
            draftId,
            (draft, _) =>
            {
                draft.State = draft.State.WithMutatedActivity(
                    nodeId,
                    node => node with
                    {
                        Inputs = [.. node.Inputs.Where(i => i.ReferenceKey != inputReferenceKey)],
                    }
                );

                var lifecycleEvent = new OnActivityInputRemovedFromDraft(
                    draftId,
                    nodeId,
                    inputReferenceKey
                );

                return ValueTask.FromResult<ILifecycleEvent>(lifecycleEvent);
            },
            cancellationToken
        );
    }
}
