using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// Mutates the Draft's layout sibling (<see cref="WorkflowDefinitionDraftLayout"/>) only —
/// <see cref="Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState"/> is layout-free
/// per Elsa §E2.9.2. Publishes <see cref="OnActivityMovedInDraft"/>.
/// </summary>
public sealed class MoveActivityInDraftCommand(DraftMutationPipeline pipeline) : IMoveActivityInDraftCommand
{
    public Task Execute(
        string draftId,
        string nodeId,
        double newX,
        double newY,
        double? newWidth = null,
        double? newHeight = null,
        CancellationToken cancellationToken = default
    )
    {
        return pipeline.ExecuteMutation(
            draftId,
            (_, dbContext) => Execute(dbContext, draftId, nodeId, newX, newY, newWidth, newHeight, cancellationToken),
            cancellationToken
        );
    }

    private async ValueTask<ILifecycleEvent> Execute(
        WorkflowsDesignDbContext dbContext,
        string draftId,
        string nodeId,
        double newX,
        double newY,
        double? newWidth,
        double? newHeight,
        CancellationToken cancellationToken
    )
    {
        var layout = await dbContext.WorkflowDefinitionDraftLayouts.FirstOrDefaultAsync(
            l => l.WorkflowDefinitionDraftId == draftId, cancellationToken
        )
        ?? throw new InvalidOperationException($"Layout sibling not found for draft '{draftId}'");

        var existing = layout.Records.FirstOrDefault(r => r.NodeId == nodeId);

        var movedRecord = new DesignMetadataRecord(
            NodeId: nodeId,
            X: newX,
            Y: newY,
            Width: newWidth ?? existing?.Width,
            Height: newHeight ?? existing?.Height,
            AdditionalProperties: existing?.AdditionalProperties
        );

        layout.Records =
        [
            .. layout.Records.Where(r => r.NodeId != nodeId),
            movedRecord,
        ];

        return new OnActivityMovedInDraft(
            draftId,
            nodeId,
            newX,
            newY,
            newWidth,
            newHeight
        );
    }
}
