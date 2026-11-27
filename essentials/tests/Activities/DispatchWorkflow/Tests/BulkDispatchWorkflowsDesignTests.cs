using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Design.Models;
using Elsa.Activities.DispatchWorkflow.Design.Services;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;
using BulkDispatchWorkflowsActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>The design-side contract of <c>BulkDispatchWorkflows</c> (spec 1242 FR-001 to FR-003, FR-008, FR-010).</summary>
public sealed class BulkDispatchWorkflowsDesignTests
{
    private const string BulkVersion = "activity-version-bulk";
    private const string DispatchVersion = "activity-version-dispatch";
    private const string LeafVersion = "activity-version-leaf";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly BulkDispatchWorkflowsStructureHandler _handler = new();

    private readonly BulkDispatchSlotValidator _validator = new(
        new DefaultActivityStructureService([new BulkDispatchWorkflowsStructureHandler()]),
        new TypeKeyActivityCatalog()
            .Add(BulkVersion, typeof(BulkDispatchWorkflowsActivity).FullName!)
            .Add(DispatchVersion, typeof(DispatchWorkflowActivity).FullName!)
            .Add(LeafVersion, "Test.Leaf"));

    [Fact]
    public void The_activity_has_its_type_key_three_single_slots_and_no_fault_handler()
    {
        Assert.Equal("Elsa.BulkDispatchWorkflows", ActivityTypeMetadata.GetDeclaredActivityType(typeof(BulkDispatchWorkflowsActivity)));
        Assert.Equal(BulkDispatchWorkflowsConstants.CatalogTypeKey, typeof(BulkDispatchWorkflowsActivity).FullName);
        var slots = typeof(BulkDispatchWorkflowsActivity).GetCustomAttributes(typeof(ActivityChildSlotAttribute), false)
            .Cast<ActivityChildSlotAttribute>()
            .Select(slot => (slot.Name, slot.Property, slot.Cardinality))
            .ToArray();
        Assert.Equal(
            [
                ("BulkDispatchWorkflows.Dispatch", "dispatch", ActivityChildSlotCardinalities.Single),
                ("BulkDispatchWorkflows.ChildCompleted", "childCompleted", ActivityChildSlotCardinalities.Single),
                ("BulkDispatchWorkflows.ChildFaulted", "childFaulted", ActivityChildSlotCardinalities.Single)
            ],
            slots);

        // FR-008: the incident strategy decides every child fault, so the activity handles none itself.
        Assert.False(typeof(IRuntimeActivityChildFaultHandler).IsAssignableFrom(typeof(BulkDispatchWorkflowsActivity)));
        Assert.False(typeof(IRuntimeActivityDescendantFaultHandler).IsAssignableFrom(typeof(BulkDispatchWorkflowsActivity)));
        // It keeps waiting for a faulted child without consuming its fault, so RescheduleActivity can end the pass.
        Assert.True(typeof(IRuntimeAcceptsRescheduledFaultedChild).IsAssignableFrom(typeof(BulkDispatchWorkflowsActivity)));
        Assert.Equal(16, BulkDispatchWorkflowsConstants.Window);
        Assert.Equal(1000, BulkDispatchWorkflowsConstants.ItemCap);
        Assert.Equal(["Items"], typeof(BulkDispatchWorkflowsActivity).GetProperties()
            .Where(property => property.IsDefined(typeof(ActivityInputAttribute), false)).Select(property => property.Name));
    }

    [Fact]
    public void The_handler_projects_replaces_compiles_and_remaps_the_three_slots()
    {
        var bulk = Bulk(dispatch: Leaf("dispatch"), completed: Leaf("completed"), faulted: Leaf("faulted"));

        var projections = _handler.ProjectChildren(bulk);
        Assert.Equal(
            [("BulkDispatchWorkflows.Dispatch", "dispatch"), ("BulkDispatchWorkflows.ChildCompleted", "completed"), ("BulkDispatchWorkflows.ChildFaulted", "faulted")],
            projections.Select(slot => (slot.Name, Assert.Single(slot.Activities).NodeId)));

        var replaced = _handler.ReplaceChildren(bulk, projections.Select(slot => slot.Name == BulkDispatchWorkflowsConstants.ChildFaultedSlotName
            ? new ActivityChildProjection(slot.Name, []) : slot).ToArray());
        Assert.Empty(_handler.ProjectChildren(replaced).Single(slot => slot.Name == BulkDispatchWorkflowsConstants.ChildFaultedSlotName).Activities);

        var compiled = _handler.CompileExecutableStructure(bulk);
        var remapped = _handler.RemapExecutableStructure(compiled, new Dictionary<string, string> { ["dispatch"] = "x-dispatch", ["faulted"] = "x-faulted" })
            .Payload.Deserialize<BulkDispatchWorkflowsExecutableStructure>(Web)!;
        Assert.Equal(("x-dispatch", "completed", "x-faulted"), (remapped.Dispatch, remapped.ChildCompleted, remapped.ChildFaulted));
    }

    [Fact]
    public void The_handler_states_that_passes_run_concurrently_and_declares_the_item_variables()
    {
        var bulk = Bulk(dispatch: Leaf("dispatch"), completed: Leaf("completed"));

        // FR-010: a binding between two different children cannot tell which item's run it reads.
        Assert.Equal(
            new ActivityChildResultFacts(ActivityChildResultAvailability.Unknown, MayRunConcurrently: true) { EarlierProducerRunCanReachConsumer = true },
            _handler.AssessChildResult(bulk, "dispatch", "completed"));
        // A child with itself: each pass is scheduled by the activity, so bindings inside one slot keep their rules.
        Assert.Equal(
            new ActivityChildResultFacts(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = false },
            _handler.AssessChildResult(bulk, "completed", "completed"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(bulk, "dispatch", "completed"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _handler.RelateChildren(bulk, "completed", "completed"));
        Assert.True(_handler.CompletesAfterItsChildren(bulk));
        Assert.Equal(["currentItem", "currentIndex"], _handler.ProjectIterationVariables(bulk).Select(variable => variable.Name));
    }

    [Fact]
    public async Task A_Dispatch_slot_holding_one_Dispatch_Workflow_is_valid()
    {
        Assert.Empty(await ValidateAsync(Bulk(dispatch: Node("dispatch", DispatchVersion), completed: Leaf("completed"))));
    }

    [Fact]
    public async Task An_empty_Dispatch_slot_is_reported_and_tells_the_author_what_to_drop_there()
    {
        // A new bulk node from the toolbox carries the activity's version id and null slots.
        var errors = await ValidateAsync(new ActivityNode("bulk", BulkVersion, [], []));

        var error = Assert.Single(errors);
        Assert.Equal(("bulk", "BulkDispatch/DispatchSlot"), (error.Path, error.Type));
        Assert.Contains("Drop a Dispatch Workflow there", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Dispatch_slot_holding_another_activity_is_reported()
    {
        var errors = await ValidateAsync(Bulk(dispatch: Leaf("leaf")));

        var error = Assert.Single(errors);
        Assert.Equal(("bulk", "BulkDispatch/DispatchSlot"), (error.Path, error.Type));
        Assert.Contains("must hold one Dispatch Workflow", error.Message, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateAsync(ActivityNode root) =>
        [.. await _validator.Validate(new StubDraft(new WorkflowDefinitionState([], root, [], [], null)), CancellationToken.None)];

    private static ActivityNode Leaf(string nodeId) => Node(nodeId, LeafVersion);

    private static ActivityNode Node(string nodeId, string versionId) => new(nodeId, versionId, [], []);

    private static ActivityNode Bulk(ActivityNode? dispatch = null, ActivityNode? completed = null, ActivityNode? faulted = null) =>
        new("bulk", BulkVersion, [], [], new ActivityNodeStructure(
            BulkDispatchWorkflowsConstants.StructureKind,
            BulkDispatchWorkflowsConstants.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new BulkDispatchWorkflowsAuthoredStructure(dispatch, completed, faulted), Web)));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
