using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeGeneratorContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GeneratorRegistration_IsScopedToLongLivedActivityExecution()
    {
        var registration = NewGeneratorRegistration();

        Assert.Equal("generator-1", registration.GeneratorId);
        Assert.Equal("wfexec-1", registration.WorkflowExecutionId);
        Assert.Equal("actexec-generator", registration.GeneratorActivityExecutionId);
        Assert.Equal("actexec-scope", registration.OwningScopeActivityExecutionId);
        Assert.Equal("branch-a", registration.BranchId);
        Assert.Equal(GeneratorStatus.Active, registration.Status);
        Assert.Equal(GeneratorStopPolicy.ScopeEnd, registration.StopPolicy);
        Assert.Equal(GeneratorBackpressurePolicy.Pause, registration.BackpressurePolicy);
    }

    [Fact]
    public void SchedulerState_CarriesGeneratedEventsSeparatelyFromActivitySchedulingCompletionAndContinuations()
    {
        var downstreamActivityWork = new ScheduledActivityWorkItem(
            WorkItemId: "activity-work-1",
            WorkflowExecutionId: "wfexec-1",
            ExecutableNodeId: "node-downstream",
            ActivityExecutionId: "actexec-downstream",
            SchedulingActivityExecutionId: "actexec-generator",
            BranchId: "branch-a",
            IterationId: null,
            EnqueuedAt: _now.AddSeconds(1),
            Reason: "GeneratedEventScheduledDownstreamActivity");
        var generatedEventWork = NewGeneratedEventWork(sequence: 1);

        var scheduler = new SchedulerState(
            workflowExecutionId: "wfexec-1",
            version: 6,
            pendingWork: [downstreamActivityWork],
            pendingContinuations:
            [
                new SchedulerContinuationWorkItem(
                    workItemId: "continuation-1",
                    workflowExecutionId: "wfexec-1",
                    activityExecutionId: "actexec-wait",
                    branchId: "branch-a",
                    kind: SchedulerContinuationKind.VolatileWaitCompleted,
                    volatileWaitId: "wait-1",
                    enqueuedAt: _now,
                    reason: "VolatileWaitCompleted")
            ],
            volatileWaits: [],
            pendingCompletionWork:
            [
                new SchedulerCompletionWorkItem(
                    workItemId: "completion-1",
                    workflowExecutionId: "wfexec-1",
                    subjectActivityExecutionId: "actexec-generator",
                    completedChildActivityExecutionId: null,
                    branchId: "branch-a",
                    kind: SchedulerCompletionKind.ActivityCompleted,
                    sequence: 10,
                    enqueuedAt: _now,
                    reason: "ActivityCompleted")
            ],
            activeGenerators: [NewGeneratorRegistration()],
            pendingGeneratedEvents: [generatedEventWork]);

        Assert.Single(scheduler.ActiveGenerators);
        var generated = Assert.Single(scheduler.PendingGeneratedEvents);
        Assert.Equal("event-1", generated.GeneratedEvent.GeneratedEventId);
        Assert.Equal("actexec-generator", generated.GeneratorActivityExecutionId);
        Assert.Single(scheduler.PendingWork);
        Assert.Single(scheduler.PendingCompletionWork);
        Assert.Single(scheduler.PendingContinuations);
    }

    [Fact]
    public void GeneratedEvent_IsEmissionDataNotActivityExecutionState()
    {
        var payload = new RuntimeDurableValueReference("durable-generated-payload");
        var generatedEvent = NewGeneratedEvent(sequence: 3, payloadValue: payload);
        var downstreamActivityWork = new ScheduledActivityWorkItem(
            WorkItemId: "activity-work-3",
            WorkflowExecutionId: generatedEvent.WorkflowExecutionId,
            ExecutableNodeId: "node-downstream",
            ActivityExecutionId: "actexec-downstream-3",
            SchedulingActivityExecutionId: generatedEvent.GeneratorActivityExecutionId,
            BranchId: generatedEvent.BranchId,
            IterationId: null,
            EnqueuedAt: _now.AddSeconds(1),
            Reason: "GeneratedEventScheduledDownstreamActivity");

        Assert.Equal("actexec-generator", generatedEvent.GeneratorActivityExecutionId);
        Assert.Equal("durable-generated-payload", generatedEvent.PayloadValue!.ValueId);
        Assert.Equal("actexec-downstream-3", downstreamActivityWork.ActivityExecutionId);
        Assert.NotEqual(generatedEvent.GeneratorActivityExecutionId, downstreamActivityWork.ActivityExecutionId);
    }

    [Fact]
    public void SchedulerGeneratedEventWork_DerivesWorkflowAndGeneratorIdentityFromGeneratedEvent()
    {
        var generatedEvent = NewGeneratedEvent(sequence: 4);
        var workItem = new SchedulerGeneratedEventWorkItem(
            workItemId: "generated-work-4",
            generatedEvent: generatedEvent,
            enqueuedAt: _now.AddSeconds(1),
            reason: "GeneratorEmitted",
            metadata: new Dictionary<string, string> { ["Lane"] = "GeneratedEvents" });

        Assert.Equal("wfexec-1", workItem.WorkflowExecutionId);
        Assert.Equal("actexec-generator", workItem.GeneratorActivityExecutionId);
        Assert.Equal("branch-a", workItem.BranchId);
        Assert.Equal("GeneratedEvents", workItem.Metadata["Lane"]);
    }

    [Fact]
    public void SchedulerState_NormalizesMissingGeneratorCollectionsToEmpty()
    {
        var scheduler = new SchedulerState(
            workflowExecutionId: "wfexec-1",
            version: 1,
            activeGenerators: null,
            pendingGeneratedEvents: null);

        Assert.Empty(scheduler.ActiveGenerators);
        Assert.Empty(scheduler.PendingGeneratedEvents);
    }

    [Fact]
    public void SchedulerState_RecordCopyNormalizesGeneratorCollectionAssignments()
    {
        var scheduler = new SchedulerState(
            workflowExecutionId: "wfexec-1",
            version: 1);
        var activeGenerators = new List<GeneratorRegistration> { NewGeneratorRegistration() };
        var pendingGeneratedEvents = new List<SchedulerGeneratedEventWorkItem> { NewGeneratedEventWork(sequence: 1) };

        var copied = scheduler with
        {
            ActiveGenerators = activeGenerators,
            PendingGeneratedEvents = pendingGeneratedEvents
        };
        activeGenerators.Clear();
        pendingGeneratedEvents.Clear();

        Assert.Single(copied.ActiveGenerators);
        Assert.Single(copied.PendingGeneratedEvents);
    }

    [Fact]
    public void GeneratorRegistration_RejectsInvalidExpiration()
    {
        Assert.Throws<ArgumentException>(() => new GeneratorRegistration(
            generatorId: "generator-1",
            workflowExecutionId: "wfexec-1",
            generatorActivityExecutionId: "actexec-generator",
            owningScopeActivityExecutionId: "actexec-scope",
            branchId: "branch-a",
            registeredAt: _now,
            expiresAt: _now));
    }

    [Fact]
    public void TimeWindowGeneratorStopPolicy_RequiresExpiration()
    {
        var exception = Assert.Throws<ArgumentException>(() => new GeneratorRegistration(
            generatorId: "generator-1",
            workflowExecutionId: "wfexec-1",
            generatorActivityExecutionId: "actexec-generator",
            owningScopeActivityExecutionId: "actexec-scope",
            branchId: "branch-a",
            registeredAt: _now,
            stopPolicy: GeneratorStopPolicy.TimeWindow));

        Assert.Contains("expiration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeneratedEvent_RejectsNegativeSequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewGeneratedEvent(sequence: -1));
    }

    [Fact]
    public void GeneratedEvent_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() => new GeneratedEvent(
            generatedEventId: "event-1",
            workflowExecutionId: "wfexec-1",
            generatorActivityExecutionId: "actexec-generator",
            branchId: "branch-a",
            name: " ",
            sequence: 1,
            occurredAt: _now,
            durability: GeneratedEventDurability.PolicyControlled));
    }

    private GeneratorRegistration NewGeneratorRegistration() => new(
        generatorId: "generator-1",
        workflowExecutionId: "wfexec-1",
        generatorActivityExecutionId: "actexec-generator",
        owningScopeActivityExecutionId: "actexec-scope",
        branchId: "branch-a",
        registeredAt: _now,
        metadata: new Dictionary<string, string> { ["Kind"] = "timer" });

    private GeneratedEvent NewGeneratedEvent(long sequence, RuntimeDurableValueReference? payloadValue = null) => new(
        generatedEventId: $"event-{sequence}",
        workflowExecutionId: "wfexec-1",
        generatorActivityExecutionId: "actexec-generator",
        branchId: "branch-a",
        name: "Tick",
        sequence: sequence,
        occurredAt: _now.AddSeconds(sequence),
        durability: GeneratedEventDurability.PolicyControlled,
        payloadValue: payloadValue,
        metadata: new Dictionary<string, string> { ["Generator"] = "timer" });

    private SchedulerGeneratedEventWorkItem NewGeneratedEventWork(long sequence) => new(
        workItemId: $"generated-work-{sequence}",
        generatedEvent: NewGeneratedEvent(sequence),
        enqueuedAt: _now.AddSeconds(sequence),
        reason: "GeneratorEmitted");
}
