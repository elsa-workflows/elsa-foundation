using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The completion outcome an activity commits must also reach the <em>inspection projection</em> — the read model
/// every observability surface (instance detail, the Studio timeline) reads. Durable state and the read model
/// disagreeing means an operator sees a Completed node that appears to have taken no port.
/// </summary>
/// <remarks>
/// Written while investigating
/// <see href="https://github.com/elsa-workflows/elsa-foundation/issues/1127">#1127</see>, where a real server
/// reported <c>outcomeNames: []</c> on a workflow's ROOT activity while its durable
/// <c>runtime.completionOutcomeNames</c> carried the truth. These four cases cover the two axes that looked
/// implicated — root vs nested, completed inline vs resumed from a bookmark — and all four are <b>correct</b> on
/// the in-process runtime over the in-memory stores. That is the point of keeping them: they pin the behaviour
/// that already holds here, and they narrow #1127 to the parts of a real composition they do not cover
/// (Groundwork-backed projection storage, the coalescing persistence decorator, the REST read path).
/// </remarks>
public sealed class ActivityExecutionInspectionOutcomeTests
{
    private const string LeafNodeId = "node-leaf";
    private const string RootNodeId = "node-root";
    private const string EventName = "go";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inline_completion_projects_its_outcome(bool nested)
    {
        await using var harness = NewHarness();
        var leaf = NewLeaf(typeof(WriteLine), new Dictionary<string, object?> { ["text"] = "hi" });
        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(nested ? NewSequenceRoot(leaf) : leaf));

        await AssertProjectedOutcomeAsync(harness, run, LeafNodeId, ActivityOutcomes.Done);
        if (nested)
            await AssertProjectedOutcomeAsync(harness, run, RootNodeId, ActivityOutcomes.Done);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resumed_completion_projects_its_outcome(bool nested)
    {
        await using var harness = NewHarness();
        var scopedResumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(LeafNodeId, Event.ResumeTargetId);
        var leaf = NewLeaf(typeof(Event), new Dictionary<string, object?>
        {
            [nameof(Event.EventName)] = EventName,
            [nameof(Event.CanStartWorkflow)] = false
        });

        var executable = new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: nested ? NewSequenceRoot(leaf) : leaf,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [scopedResumeTargetId] = new(
                    scopedResumeTargetId,
                    LeafNodeId,
                    "ResumeAsync",
                    new Dictionary<string, string>(),
                    Event.ResumeTargetId)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

        var suspended = (await harness.RunAsync(executable)).State(LeafNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, suspended.Status);

        var run = await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            bookmarkId: Assert.Single(suspended.BookmarkIds),
            activityExecutionId: suspended.InvocationId,
            executableNodeId: LeafNodeId,
            resumeTargetId: scopedResumeTargetId,
            stimulusType: EventStimulus.StimulusType,
            stimulusHash: EventStimulus.Hash(EventName),
            input: JsonSerializer.SerializeToElement(new EventReceived(EventName)));

        await AssertProjectedOutcomeAsync(harness, run, LeafNodeId, ActivityOutcomes.Done);
        if (nested)
            await AssertProjectedOutcomeAsync(harness, run, RootNodeId, ActivityOutcomes.Done);
    }

    /// <summary>
    /// Asserts the projection agrees with committed state, and reports both sides when it does not — the whole
    /// failure mode is the two disagreeing, so naming only one of them would make a failure unreadable.
    /// </summary>
    private static async Task AssertProjectedOutcomeAsync(
        WorkflowExecutionHarness harness,
        WorkflowExecutionRun run,
        string executableNodeId,
        string expectedOutcome)
    {
        var state = run.AssertCompleted(executableNodeId);
        Assert.Equal([expectedOutcome], WorkflowExecutionRun.CompletionOutcomes(state));

        var projection = await harness.Services.GetRequiredService<IActivityExecutionInspectionStore>()
            .FindAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);

        Assert.True(
            projection is not null,
            $"No inspection projection was recorded for '{executableNodeId}'.");
        Assert.True(
            projection!.OutcomeNames.Contains(expectedOutcome, StringComparer.Ordinal),
            $"'{executableNodeId}' committed outcome '{expectedOutcome}' but its inspection projection reports " +
            $"[{string.Join(", ", projection.OutcomeNames)}] (last checkpoint '{projection.LastCheckpointId}'). " +
            "Durable state and the read model disagree: every observability surface would show this node as " +
            "having taken no port.");
    }

    private static WorkflowExecutionHarness NewHarness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
        .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
        .Build("actexec-root", "actexec-leaf");

    private static ExecutableNode NewLeaf(Type activityType, IReadOnlyDictionary<string, object?> inputs) =>
        new(
            executableNodeId: LeafNodeId,
            authoredActivityId: $"authored-{LeafNodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: inputs.ToDictionary(
                item => item.Key,
                item => NewLiteral(item.Key, item.Value),
                StringComparer.Ordinal),
            metadata: new Dictionary<string, string>());

    private static ExecutableNode NewSequenceRoot(ExecutableNode child) =>
        new(
            executableNodeId: RootNodeId,
            authoredActivityId: $"authored-{RootNodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [child])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = new[] { child.ExecutableNodeId } })));

    private static RuntimeInputBinding NewLiteral(string key, object? value)
    {
        var type = new ValueTypeDescriptor(value switch
        {
            bool => "Boolean",
            string => "String",
            _ => "Object"
        });
        return new RuntimeInputBinding(
            key,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: value is null
                ? ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline)
                : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value, value.GetType()), ValueProtectionPolicy.InstanceInline));
    }
}
