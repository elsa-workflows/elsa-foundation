using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class AddActivityToDraftCommand(DraftMutationPipeline pipeline) : IAddActivityToDraftCommand
{
    public Task Execute(string draftId, ActivityNode activity, CancellationToken cancellationToken = default)
    {
        return pipeline.ExecuteMutation(
            draftId, 
            (draft, _) =>
            {
                var activities = draft.State.Activities.Append(activity);
                draft.State = draft.State with { Activities = activities };

                return ValueTask.FromResult<IEvent>(
                    new OnActivityAddedToDraft(draftId, activity)
                );
            }, 
            cancellationToken
        );
    }
}
