using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkCreateDraftCommand(
    GroundworkDraftCreationCoordinator coordinator,
    IPayloadSerializer payloadSerializer)
    : ICreateDraftCommand
{
    private const string OperationKind = "workflow.draft.create.v1";

    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string workflowDefinitionId,
        WorkflowDefinitionState? initialState = null,
        IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null,
        string? sourceVersionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        var state = initialState ?? EmptyState();
        var layout = initialLayout?.ToArray() ?? [];
        var requestMaterial = new CreateDraftRequestMaterial(
            workflowDefinitionId,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition draft",
                () => payloadSerializer.Serialize(state)),
            layout.Select(ToMaterial).ToArray(),
            sourceVersionId);
        return await coordinator.ExecuteAsync(
            operationKey,
            OperationKind,
            requestMaterial,
            _ => Task.FromResult(new GroundworkDraftCreationInput(
                workflowDefinitionId,
                state,
                layout,
                sourceVersionId)),
            cancellationToken);
    }

    private static WorkflowDefinitionState EmptyState() => new(
        Variables: [],
        RootActivity: null,
        Inputs: [],
        Outputs: [],
        StrategyOptions: null);

    private static LayoutMaterial ToMaterial(DesignMetadataRecord record) =>
        new(
            record.NodeId,
            record.X,
            record.Y,
            record.Width,
            record.Height,
            record.AdditionalProperties?.GetRawText());

    private sealed record CreateDraftRequestMaterial(
        string WorkflowDefinitionId,
        string StateJson,
        IReadOnlyCollection<LayoutMaterial> Layout,
        string? SourceVersionId);

    private sealed record LayoutMaterial(
        string NodeId,
        double X,
        double Y,
        double? Width,
        double? Height,
        string? AdditionalPropertiesJson);
}
