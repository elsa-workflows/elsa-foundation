using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using ArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// Shared builders + drivers for the Unit 2 <c>IUpdateDraftCommand</c> diff tests. Keeps every
/// diff test file terse: seed an empty Draft, then submit a complete desired
/// <see cref="WorkflowDefinitionState"/> (+ optional layout) and assert on the published per-diff
/// events / persisted state. Full-state-always (FR-001) — there is no patch API.
/// </summary>
internal static class UpdateDraftTestKit
{
    private const string ActivitiesSlotName = "Activities";
    private const string ConnectionsSlotName = "Connections";
    private const string StartActivityNodeIdMetadataKey = "StartActivityNodeId";

    public static async Task<string> SeedEmptyDraft(WorkflowsDesignTestHost host, string workflowDefinitionId = "wf-1")
    {
        await host.EnsureDefinition(workflowDefinitionId);
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute(workflowDefinitionId);
    }

    public static async Task Update(
        WorkflowsDesignTestHost host,
        string draftId,
        WorkflowDefinitionState state,
        IReadOnlyCollection<DesignMetadataRecord>? layout = null)
    {
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<IUpdateDraftCommand>();
        await command.Execute(new UpdateDraftRequest(draftId, state, layout ?? []));
    }

    /// <summary>
    /// The per-diff mutation events captured at or after <paramref name="skip"/> events — only the
    /// diff emissions (the 20 <c>Elsa.Workflows.Design.Core.Events</c> mutation events) remain, in
    /// publication order. The validation-gate pair
    /// (<c>OnDraftValidating</c>/<c>OnDraftValidated</c>, a separate namespace) and infrastructure
    /// events such as <c>OnEntitySaving</c> are filtered out.
    /// </summary>
    public static IReadOnlyList<IEvent> DiffEventsSince(WorkflowsDesignTestHost host, int skip) =>
        host.EventPublisher.CapturedEvents
            .Skip(skip)
            .Where(e => e.GetType().Namespace == "Elsa.Workflows.Design.Core.Events")
            .ToList();

    public static WorkflowDefinitionState State(
        IEnumerable<VariableDefinition>? variables = null,
        IEnumerable<ActivityConnection>? connections = null,
        IEnumerable<ActivityNode>? activities = null,
        IEnumerable<InputDefinition>? inputs = null,
        IEnumerable<OutputDefinition>? outputs = null) =>
        new(
            Variables: variables ?? [],
            RootActivity: RootActivity(activities, connections),
            Inputs: inputs ?? [],
            Outputs: outputs ?? [],
            WorkflowActivityOptions: null,
            StrategyOptions: null);

    public static ActivityNode Node(
        string nodeId,
        string activityVersionId = "av-1",
        IEnumerable<ArgumentState>? inputs = null,
        IEnumerable<ArgumentState>? outputs = null,
        bool isStart = false) =>
        new(
            NodeId: nodeId,
            ActivityVersionId: activityVersionId,
            Inputs: inputs ?? [],
            Outputs: outputs ?? []);

    public static VariableDefinition Variable(string referenceKey, string name, Type? type = null) =>
        new(referenceKey, name, TypeInformation.FromType(type ?? typeof(string)), null, null);

    public static InputDefinition Input(string referenceKey, string name, Type? type = null) =>
        new(referenceKey, name, TypeInformation.FromType(type ?? typeof(string)), null, name, null);

    public static OutputDefinition Output(string referenceKey, string name, Type? type = null) =>
        new(referenceKey, name, TypeInformation.FromType(type ?? typeof(string)), null, name, null);

    public static ArgumentState Arg(string referenceKey, string? value = null) =>
        new(referenceKey, new ArgumentValue(value), null, null, null, null);

    public static ActivityConnection Connection(
        string sourceNodeId,
        string targetNodeId,
        string sourcePort = "out",
        string targetPort = "in") =>
        new(new ActivityPortConnection(sourceNodeId, sourcePort), new ActivityPortConnection(targetNodeId, targetPort));

    public static DesignMetadataRecord LayoutRecord(string nodeId, double x, double y, double? width = null, double? height = null) =>
        new(nodeId, x, y, width, height);

    private static ActivityNode? RootActivity(IEnumerable<ActivityNode>? activities, IEnumerable<ActivityConnection>? connections)
    {
        var activitySnapshot = (activities ?? []).ToArray();
        if (activitySnapshot.Length == 0)
            return null;

        var connectionSnapshot = (connections ?? []).ToArray();
        if (activitySnapshot.Length == 1 && connectionSnapshot.Length == 0)
            return activitySnapshot[0];

        var startActivityNodeId = activitySnapshot.FirstOrDefault(activity => activity.NodeId == "start")?.NodeId;
        return new ActivityNode(
            NodeId: "$root",
            ActivityVersionId: "$workflow-root",
            Inputs: [],
            Outputs: [],
            ChildSlots:
            [
                new ActivityChildSlot(
                    ActivitiesSlotName,
                    activitySnapshot,
                    startActivityNodeId is null
                        ? null
                        : new Dictionary<string, string>
                        {
                            [StartActivityNodeIdMetadataKey] = startActivityNodeId
                        })
            ],
            ConnectionSlots: connectionSnapshot.Length == 0
                ? null
                :
                [
                    new ActivityConnectionSlot(ConnectionsSlotName, connectionSnapshot)
                ]);
    }
}
