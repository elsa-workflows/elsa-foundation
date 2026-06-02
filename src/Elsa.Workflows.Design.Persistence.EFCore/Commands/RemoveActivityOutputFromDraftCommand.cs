using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class RemoveActivityOutputFromDraftCommand(DraftMutationPipeline pipeline) : IRemoveActivityOutputFromDraftCommand
{
    public Task Execute(
        string draftId,
        string nodeId,
        string outputReferenceKey,
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
                        Outputs = [.. node.Outputs.Where(o => o.ReferenceKey != outputReferenceKey)],
                    }
                );

                var lifecycleEvent = new OnActivityOutputRemovedFromDraft(
                    draftId,
                    nodeId,
                    outputReferenceKey
                );

                return ValueTask.FromResult<IEvent>(lifecycleEvent);
            },
            cancellationToken
        );
    }
}
