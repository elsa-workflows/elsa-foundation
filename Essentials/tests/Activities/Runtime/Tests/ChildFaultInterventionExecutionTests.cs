using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// A structural parent keeps a child fault blocking until an operator intervenes. The incident stays blocking with a
/// wait-for-intervention outcome in the same checkpoint as the parent's deferred continuation, no incident strategy acts
/// on it, and the faulted child's subtree is left as it is.
/// </summary>
public sealed class ChildFaultInterventionExecutionTests
{
    [Fact]
    public async Task LeafChildFault_InterventionRequested_IncidentWaitsAndRunStaysRunningUnderTheFaultStrategy()
    {
        await using var harness = NewHarness(new InterventionDirective(), out var log);

        var run = await harness.RunAsync(NewExecutable(NewParent(InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity)))));

        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-parent").Status);
        var faulted = run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted.Status);
        var incident = Assert.Single(await ListIncidentsAsync(harness));
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Null(incident.ResolvedAt);
        var outcome = incident.ResolutionOutcome!;
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, outcome.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.StructuralIntervention, outcome.SystemSource);
        Assert.Null(outcome.Strategy);
        Assert.Equal("scope-1", outcome.Metadata["test.scope"]);
        Assert.Equal("actexec-parent", incident.Metadata[RuntimeMetadataKeys.InterventionRequestedBy]);
        Assert.Equal("test-intervene", incident.Metadata[RuntimeMetadataKeys.InterventionReason]);
        Assert.Equal(faulted.Execution.ActivityExecutionId, Assert.Single(log.Faults).ChildActivityExecutionId);
    }

    [Fact]
    public async Task ChildFault_CarriesTheCommittedFaultCodeAndCategory()
    {
        await using var harness = NewHarness(new InterventionDirective(), out var log);

        await harness.RunAsync(NewExecutable(NewParent(InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity)))));

        var fault = Assert.Single(log.Faults);
        Assert.Equal(OutcomeUnknownActivity.Code, fault.FaultCode);
        Assert.Equal(OutcomeUnknownActivity.Category, fault.FaultCategory);
        Assert.NotNull(fault.IncidentId);
    }

    [Fact]
    public async Task ChildFault_WithoutCategory_ReportsTheCodeAndNoCategory()
    {
        await using var harness = NewHarness(new InterventionDirective(), out var log);

        await harness.RunAsync(NewExecutable(NewParent(WorkflowExecutionHarness.NewFaultingNode("node-f"))));

        var fault = Assert.Single(log.Faults);
        Assert.Equal("TEST-FAULT", fault.FaultCode);
        Assert.Null(fault.FaultCategory);
    }

    [Fact]
    public async Task CompositeChildFault_Intervention_DoesNotCancelTheFaultedChildsSubtree()
    {
        // ContinueWithIncidents keeps the run alive past the grandchild's own incident, which nothing absorbs.
        await using var harness = NewHarness(new InterventionDirective(), out _, "actexec-parent", "actexec-b", "actexec-b-wait", "actexec-b-fault");

        var run = await harness.RunAsync(NewExecutable(
            NewParent(StructuralExecutionTestSupport.NewStructuralNode("node-b", typeof(RefaultingForkActivity),
                StructuralExecutionTestSupport.NewWaitingNode("node-b-wait"),
                WorkflowExecutionHarness.NewFaultingNode("node-b-fault"))),
            IncidentStrategyBuiltIns.ContinueWithIncidentsReference,
            waitingNodeIds: ["node-b-wait"]));

        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-b").Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, run.State("node-b-wait").Status);
        Assert.NotEmpty(await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        var incidents = await ListIncidentsAsync(harness);
        var waiting = Assert.Single(incidents, incident => incident.ExecutableNodeId == "node-b");
        Assert.Equal(IncidentStatus.Blocking, waiting.Status);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, waiting.ResolutionOutcome!.ActionKind);
        Assert.DoesNotContain(incidents, incident => incident.Status == IncidentStatus.Suppressed);
    }

    [Theory]
    [InlineData(nameof(InterventionDirective.AbsorbToo), "cannot both absorb the child fault and request intervention")]
    [InlineData(nameof(InterventionDirective.StageTwice), "cannot request intervention more than once")]
    [InlineData(nameof(InterventionDirective.CompleteInsteadOfDefer), "requires a Defer continuation")]
    [InlineData(nameof(InterventionDirective.OverrideIncidentId), "but this evaluation carries incident")]
    public async Task Misuse_FaultsTheEvaluation(string misuse, string expectedMessage)
    {
        var directive = misuse switch
        {
            nameof(InterventionDirective.AbsorbToo) => new InterventionDirective { AbsorbToo = true },
            nameof(InterventionDirective.StageTwice) => new InterventionDirective { StageTwice = true },
            nameof(InterventionDirective.CompleteInsteadOfDefer) => new InterventionDirective { CompleteInsteadOfDefer = true },
            _ => new InterventionDirective { OverrideIncidentId = "incident-wrong" }
        };
        await using var harness = NewHarness(directive, out _);

        var run = await harness.RunAsync(
            NewExecutable(NewParent(InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity)))),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains(expectedMessage, parent.Fault!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(await ListIncidentsAsync(harness), incident =>
            incident.ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.WaitForIntervention &&
            incident.ResolutionOutcome.SystemSource == IncidentResolutionSystemSources.StructuralIntervention);
    }

    [Fact]
    public async Task InterventionInCompletionEvaluation_FaultsTheEvaluation()
    {
        await using var harness = NewHarness(new InterventionDirective { InterveneOnCompletion = true }, out _);

        var run = await harness.RunAsync(
            NewExecutable(NewParent(WorkflowExecutionHarness.NewProbeNode("node-a"))),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("only valid in a child-fault evaluation", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignedOperationKeys_InScheduleMetadata_ReachTheChildContext(bool explicitProvenance)
    {
        await using var harness = NewHarness(new InterventionDirective { ExplicitProvenance = explicitProvenance }, out _, out var keys);

        var run = await harness.RunAsync(NewExecutable(NewParent(InterventionTestSupport.NewLeafNode("node-k", typeof(OperationKeyProbeActivity)))));

        run.AssertWorkflowCompleted();
        var observed = Assert.Single(keys.Observed);
        Assert.Equal("op/node-k", observed.OperationKey);
        Assert.Equal("target/node-k", observed.TargetOperationKey);
        var child = run.State("node-k");
        Assert.Equal("op/node-k", child.Provenance.Metadata[RuntimeMetadataKeys.OperationKey]);
        Assert.Equal("target/node-k", child.Provenance.Metadata[RuntimeMetadataKeys.TargetOperationKey]);
    }

    [Fact]
    public async Task OperationKeyAssignedToACompositeChild_IsNotInheritedByTheActivitiesInsideIt()
    {
        var recorder = new EnqueuedWorkItemRecorder();
        var keyLog = new OperationKeyLog();
        await using var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new InterventionDirective());
                services.AddSingleton(new ChildEvaluationLog());
                services.AddSingleton(keyLog);
                RecordingSchedulerWorkQueue.Register(services, recorder);
            })
            .Build("actexec-parent", "actexec-c", "actexec-leaf");

        var run = await harness.RunAsync(NewExecutable(NewParent(
            StructuralExecutionTestSupport.NewStructuralNode("node-c", typeof(PassthroughStructuralActivity),
                InterventionTestSupport.NewLeafNode("node-leaf", typeof(OperationKeyProbeActivity))))));

        run.AssertWorkflowCompleted();
        Assert.Equal("op/node-c", run.State("node-c").Provenance.Metadata[RuntimeMetadataKeys.OperationKey]);
        var leaf = Assert.Single(keyLog.Observed);
        Assert.Equal("actexec-leaf", leaf.InvocationId);
        Assert.Equal("actexec-leaf", leaf.OperationKey);
        Assert.Null(leaf.TargetOperationKey);
        var leafSchedule = Assert.Single(recorder.Items, item =>
            item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
            item.CommandMetadata.GetValueOrDefault(RuntimeMetadataKeys.ChildExecutableNodeId) == "node-leaf");
        Assert.DoesNotContain(RuntimeMetadataKeys.OperationKey, leafSchedule.CommandMetadata.Keys);
        Assert.DoesNotContain(RuntimeMetadataKeys.TargetOperationKey, leafSchedule.CommandMetadata.Keys);
    }

    private static WorkflowExecutionHarness NewHarness(InterventionDirective directive, out ChildEvaluationLog log, params string[] activityExecutionIds) =>
        NewHarness(directive, out log, out _, activityExecutionIds);

    private static WorkflowExecutionHarness NewHarness(
        InterventionDirective directive,
        out ChildEvaluationLog log,
        out OperationKeyLog keys,
        params string[] activityExecutionIds)
    {
        var childLog = new ChildEvaluationLog();
        var keyLog = new OperationKeyLog();
        log = childLog;
        keys = keyLog;
        return WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .ConfigureServices(services =>
            {
                services.AddSingleton(directive);
                services.AddSingleton(childLog);
                services.AddSingleton(keyLog);
                services.AddSingleton(new FailureBudget(int.MaxValue));
            })
            .Build(activityExecutionIds.Length == 0 ? ["actexec-parent", "actexec-child"] : activityExecutionIds);
    }

    private static ExecutableNode NewParent(ExecutableNode child) =>
        StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(InterventionParentActivity), child);

    private static WorkflowExecutable NewExecutable(
        ExecutableNode root,
        IncidentStrategyReference? incidentStrategy = null,
        IReadOnlyCollection<string>? waitingNodeIds = null)
    {
        var executable = StructuralExecutionTestSupport.NewExecutable(root, waitingNodeIds);
        return incidentStrategy is null
            ? executable
            : new WorkflowExecutable(
                executable.Identity,
                executable.RootActivity,
                executable.ResumeTargets,
                executable.CreatedAt,
                executable.CompatibilityMetadata,
                incidentStrategy);
    }

    private static async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId);
}

/// <summary>Shared test activities and node builders for the intervention and operator-retry execution tests.</summary>
internal static class InterventionTestSupport
{
    public static ExecutableNode NewLeafNode(string nodeId, Type activityType) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/leaf",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "leaf" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
}

/// <summary>What the <see cref="InterventionParentActivity"/> does on a child fault or completion.</summary>
public sealed record InterventionDirective
{
    public bool AbsorbToo { get; init; }
    public bool StageTwice { get; init; }
    public bool CompleteInsteadOfDefer { get; init; }
    public string? OverrideIncidentId { get; init; }
    public bool InterveneOnCompletion { get; init; }
    public bool ExplicitProvenance { get; init; }
    public bool AssignKeys { get; init; } = true;
    public bool KeepParentRunning { get; init; }
}

public sealed record ChildFaultEvaluation(
    string ChildActivityExecutionId,
    string ChildExecutableNodeId,
    string? IncidentId,
    string? FaultCode,
    string? FaultCategory);

public sealed record ChildCompletionEvaluation(string ChildActivityExecutionId, string ChildExecutableNodeId, string? Marker = null);

/// <summary>Records what the structural parent was told about its children, in order.</summary>
public sealed class ChildEvaluationLog
{
    private readonly List<ChildFaultEvaluation> _faults = [];
    private readonly List<ChildCompletionEvaluation> _completions = [];
    public IReadOnlyList<ChildFaultEvaluation> Faults => _faults;
    public IReadOnlyList<ChildCompletionEvaluation> Completions => _completions;
    public void Add(ChildFaultEvaluation fault) => _faults.Add(fault);
    public void Add(ChildCompletionEvaluation completion) => _completions.Add(completion);
}

public sealed record ObservedKeys(string InvocationId, string OperationKey, string? TargetOperationKey);

/// <summary>Records the operation keys every probe attempt was given.</summary>
public sealed class OperationKeyLog
{
    private readonly List<ObservedKeys> _observed = [];
    public IReadOnlyList<ObservedKeys> Observed => _observed;
    public void Add(ActivityExecutionContext context) =>
        _observed.Add(new ObservedKeys(context.InvocationId, context.OperationKey, context.TargetOperationKey));
}

/// <summary>How many more times a <see cref="OutcomeUnknownActivity"/> faults before it completes.</summary>
public sealed class FailureBudget(int failures)
{
    public int Remaining { get; private set; } = failures;
    public bool TakeFailure()
    {
        if (Remaining <= 0)
            return false;
        Remaining--;
        return true;
    }
}

/// <summary>
/// Schedules its slot children with a marker, an assigned operation key and target key in their scheduling metadata.
/// On a child fault it records the fault and keeps the incident blocking for intervention (or misuses the seam as
/// directed); on a child completion it records the completion and completes.
/// </summary>
public sealed class InterventionParentActivity(InterventionDirective directive, ChildEvaluationLog log) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildFaultHandler
{
    public const string MarkerKey = "test.marker";
    public const string SchedulingCause = "test.step";

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var parentId = context.ActivityExecutionState.InvocationId;
        foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
        {
            var metadata = new Dictionary<string, string> { [MarkerKey] = $"marker-{child.ExecutableNodeId}" };
            if (directive.AssignKeys)
            {
                metadata[RuntimeMetadataKeys.OperationKey] = $"op/{child.ExecutableNodeId}";
                metadata[RuntimeMetadataKeys.TargetOperationKey] = $"target/{child.ExecutableNodeId}";
            }
            var provenance = directive.ExplicitProvenance
                ? ActivitySchedulingProvenance.From(
                    context.WorkflowExecutionId,
                    parentId,
                    parentId,
                    branchId: null,
                    iterationId: null,
                    executionPathId: null,
                    executionScopeId: null,
                    schedulingCause: SchedulingCause,
                    metadata: new Dictionary<string, string> { [MarkerKey] = $"marker-{child.ExecutableNodeId}" })
                : null;
            context.ScheduleChildActivity(child.ExecutableNodeId, parentId, metadata, provenance);
        }

        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        log.Add(new ChildFaultEvaluation(
            context.FaultedChildActivityExecutionId,
            context.FaultedChildExecutableNodeId,
            context.IncidentId,
            context.FaultCode,
            context.FaultCategory));
        var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
        var incidentId = directive.OverrideIncidentId ?? context.IncidentId!;
        var metadata = new Dictionary<string, string> { ["test.scope"] = "scope-1" };
        runtimeContext.RequestChildFaultIntervention(incidentId, "test-intervene", metadata);
        if (directive.StageTwice)
            runtimeContext.RequestChildFaultIntervention(incidentId, "test-intervene", metadata);
        if (directive.AbsorbToo)
            runtimeContext.RequestChildFaultAbsorption(incidentId, "test-absorb");
        return ValueTask.FromResult(directive.CompleteInsteadOfDefer
            ? RuntimeStructuralContinuation.Complete()
            : RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        var commandMetadata = ((IRuntimeActivityExecutionContext)context.ParentContext).SchedulerWorkItem.CommandMetadata;
        log.Add(new ChildCompletionEvaluation(context.CompletedChildActivityExecutionId, context.CompletedChildExecutableNodeId,
            commandMetadata.GetValueOrDefault(MarkerKey)));
        if (directive.InterveneOnCompletion)
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultIntervention("incident-any", "test-intervene");
        return ValueTask.FromResult(directive.KeepParentRunning
            ? RuntimeStructuralContinuation.Defer
            : RuntimeStructuralContinuation.Complete());
    }
}

/// <summary>
/// A leaf whose outcome is unknown while its <see cref="FailureBudget"/> lasts: it faults with the reserved
/// <c>outcome-unknown</c> category, then completes. Every attempt records its operation keys.
/// </summary>
public sealed class OutcomeUnknownActivity(FailureBudget budget, OperationKeyLog keys) : Activity
{
    public const string Code = "TEST-UNCERTAIN";
    public const string Category = "outcome-unknown";

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        keys.Add(context);
        return ValueTask.FromResult(budget.TakeFailure()
            ? ActivityTransition.Fault<ActivityUnit>(new ActivityFault(Code, "The reservation response was lost.", category: Category))
            : ActivityTransition.Complete(ActivityUnit.Value));
    }
}

/// <summary>A leaf that records the operation keys it was given and completes.</summary>
public sealed class OperationKeyProbeActivity(OperationKeyLog keys) : Activity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        keys.Add(context);
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
