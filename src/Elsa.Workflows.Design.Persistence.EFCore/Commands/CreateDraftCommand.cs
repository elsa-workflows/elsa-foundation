using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.Services;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class CreateDraftCommand(IIdentityGenerator identityGenerator, DraftMutationPipeline pipeline) : ICreateDraftCommand
{
    public async Task<string> Execute(string workflowDefinitionId, WorkflowDefinitionState? initialState = null, CancellationToken cancellationToken = default)
    {
        var draftId = identityGenerator.Generate();
        var state = initialState ?? new WorkflowDefinitionState(
            Variables: [],
            ActivityConnections: [],
            Activities: [],
            Inputs: [],
            Outputs: [],
            WorkflowActivityOptions: null,
            StrategyOptions: null
        );

        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            State = state,
        };

        var layout = new WorkflowDefinitionDraftLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionDraftId = draftId,
            Records = [],
        };

        var originationEvent = new OnDraftCreated(draftId, workflowDefinitionId);

        await pipeline.ExecuteCreation(draft, layout, originationEvent, cancellationToken);

        return draftId;
    }
}
