using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// GraphActivity: the reusable-activity inlining boundary. Drives the implicit <c>Done</c> completion of an
/// unmapped boundary, and the authored boundary outcome of a mapped one.
/// </summary>
/// <remarks>
/// <para>
/// The boundary is not a CLR activity the harness can reflect over: it is activated from a pinned
/// <c>elsa.graph-activity/1</c> descriptor rather than from a type, and it declares no atomic result type. So the
/// node here carries the descriptor and contract the publish compiler stamps, exactly as
/// <c>GraphActivityProvider</c> emits them, and the runtime activates it through the real
/// <c>GraphActivityActivationStrategy</c>.
/// </para>
/// <para>
/// What this drive adds over <c>GraphActivityExecutionTests</c> is the engine: those tests call
/// <c>ExecuteStructureAsync</c>/<c>OnChildCompletedAsync</c> on a hand-made context and read the returned
/// transition, which cannot show that the scheduler ever schedules the entry node or that the selected outcome is
/// ever <em>committed</em>. Here the outcome is read back out of persisted execution state. The authoring half —
/// that publishing a reusable activity definition produces this descriptor — stays where it belongs, in
/// <c>GraphActivityProviderTests</c> and the REST e2e suite.
/// </para>
/// </remarks>
public sealed class GraphActivityDrive : IActivityDrive
{
    private const string BoundaryNodeId = "node-graph";
    private const string EntryNodeId = "node-graph-entry";
    private const string TemplateHash = "sha256:graph-template";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Type ActivityType => typeof(GraphActivity);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // No outcome mappings: the boundary completes with the implicit Done, whatever the entry emitted.
        await RunAsync(recorder, outcomeMappings: [], entryOutcome: ActivityOutcomes.Done);

        // Mapped: the entry's own outcome name is translated to the boundary's authored port.
        await RunAsync(
            recorder,
            outcomeMappings: [new GraphActivityOutcomeMappingDescriptor("Rejected", "Declined")],
            entryOutcome: "Rejected");
    }

    private static async Task RunAsync(
        ActivityDriveRecorder recorder,
        IReadOnlyList<GraphActivityOutcomeMappingDescriptor> outcomeMappings,
        string entryOutcome)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .Build("actexec-graph-boundary", "actexec-graph-entry");

        var descriptor = NewDescriptor(outcomeMappings);
        var entry = WorkflowExecutionHarness.NewProbeNode(EntryNodeId, [entryOutcome]);
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewBoundaryNode(descriptor, entry)));

        recorder.Record(typeof(GraphActivity), run, BoundaryNodeId);
    }

    private static GraphActivityDescriptor NewDescriptor(
        IReadOnlyList<GraphActivityOutcomeMappingDescriptor> outcomeMappings) =>
        new(
            definitionId: "definition-graph",
            definitionVersionId: "definition-version-graph",
            version: "1.0.0",
            templateHash: TemplateHash,
            entryNodeId: EntryNodeId,
            entryOccurrenceId: null,
            inputs: [],
            variables: [],
            outputs: [],
            outputMappings: [],
            requiredInputReferenceKeys: [],
            requiredOutputReferenceKeys: [],
            outcomeMappings: outcomeMappings);

    private static ExecutableNode NewBoundaryNode(GraphActivityDescriptor descriptor, ExecutableNode entry)
    {
        var payload = JsonSerializer.SerializeToElement(descriptor, SerializerOptions);

        // Mirrors what the publish compiler pins: an unmapped boundary offers the implicit Done port, a mapped one
        // offers exactly the boundary names its mappings declare.
        var outcomes = descriptor.OutcomeMappings.Count == 0
            ? [ActivityOutcomes.Done]
            : descriptor.OutcomeMappings
                .Select(mapping => mapping.BoundaryOutcomeName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        var contract = new ActivityContract(
            "graph",
            "1.0.0",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            outcomes,
            new ActivityActivationRequirement(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                WellKnownRuntimeActivityConsumers.GraphActivity));

        return new ExecutableNode(
            BoundaryNodeId,
            $"authored-{BoundaryNodeId}",
            "graph",
            "1.0.0",
            new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                payload),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string> { ["graph.templateHash"] = TemplateHash },
            [new ExecutableChildSlot("Graph.Entry", [entry])],
            structure: null,
            activityContract: contract);
    }
}
