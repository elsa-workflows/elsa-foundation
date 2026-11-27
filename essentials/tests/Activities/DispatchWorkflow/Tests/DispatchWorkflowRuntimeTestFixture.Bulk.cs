using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using BulkDispatchWorkflowsActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Parent executables whose root is a <c>BulkDispatchWorkflows</c> node (spec 1242).</summary>
internal sealed partial class DispatchWorkflowRuntimeTestFixture
{
    internal const string FaultingChildMessage = "fault";
    internal const string BulkNodeId = "node-bulk";
    internal const string BulkDispatchNodeId = "node-bulk-dispatch";
    internal const string CompletedSlotNodeId = "node-bulk-completed";
    internal const string FaultedSlotNodeId = "node-bulk-faulted";

    /// <summary>The slot probe faults for an item whose message is this, until <see cref="SlotFaultRepaired"/> is set.</summary>
    internal const string FaultingSlotMessage = "slot-fault";

    /// <summary>Lets a slot probe that faulted succeed on its retry.</summary>
    internal bool SlotFaultRepaired
    {
        get => _provider.GetRequiredService<ChildExecutionProbe>().SlotFaultRepaired;
        set => _provider.GetRequiredService<ChildExecutionProbe>().SlotFaultRepaired = value;
    }

    /// <summary>A child input set for one item: the child probe faults when <paramref name="fault"/> is set.</summary>
    internal static Dictionary<string, object?> BulkItem(int count, bool fault = false, string? message = null) =>
        new() { ["message"] = message ?? (fault ? FaultingChildMessage : $"item-{count}"), ["count"] = count };

    /// <summary>
    /// Submits <c>RetryFaultedActivity/1</c> for a faulted activity as an operator does, runs the alteration pump until
    /// the job ends and sweeps until the run is quiet.
    /// </summary>
    internal async ValueTask<WorkflowAlterationJobState> AlterFaultedActivityAsync(string workflowExecutionId, string sourceActivityExecutionId, string kind, int schemaVersion)
    {
        WorkflowAlterationPlanAdmissionResult admission;
        await using (var scope = _provider.CreateAsyncScope())
        {
            admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                new WorkflowAlterationSubmission(
                    new WorkflowAlterationAuthorityScope("tenant-42", "parent-caller", "root-initiator"),
                    WorkflowAlterationTargetSelector.ForExecutionIds([workflowExecutionId]),
                    [new WorkflowAlterationEnvelope(kind, schemaVersion,
                        kind == "RescheduleActivity"
                        ? JsonSerializer.SerializeToElement(new { sourceActivityExecutionId })
                        : JsonSerializer.SerializeToElement(new { sourceActivityExecutionId, reason = "Slot repaired" }))]),
                new WorkflowAlterationOperatorProvenance("operator-1", correlationId: null),
                $"{kind}:{sourceActivityExecutionId}");
        }

        var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, workflowExecutionId);
        var options = new WorkflowAlterationOrchestrationOptions { WorkerId = "test-worker" };
        WorkflowAlterationJobState? job = null;
        for (var sweep = 0; sweep < 8; sweep++)
        {
            await using (var scope = _provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>().ExecuteAsync(options, "test-worker");
                job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId);
            }

            if (job?.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
                break;
        }

        await SweepUntilQuietAsync();
        return job ?? throw new InvalidOperationException($"Alteration job '{jobId}' was not found.");
    }

    /// <summary>
    /// Starts a parent whose root is a bulk dispatch over <paramref name="items"/>. The Dispatch node passes each item as
    /// the child's inputs (a <c>currentItem</c> read). The slots, when present, record their pass in the probe.
    /// </summary>
    internal async ValueTask<WorkflowExecutionStartDispatchResult> StartBulkParentAsync(
        string caseId,
        string parentWorkflowExecutionId,
        IReadOnlyList<object?>? items,
        bool waitForCompletion,
        bool completedSlot = false,
        bool faultedSlot = false,
        IncidentStrategyReference? incidentStrategy = null,
        bool withDispatch = true,
        JsonElement? itemsJson = null)
    {
        var parentExecutable = NewBulkParentExecutable(caseId, itemsJson ?? JsonSerializer.SerializeToElement(items, SerializerOptions),
            waitForCompletion, completedSlot, faultedSlot, incidentStrategy, withDispatch);
        var parentReference = NewSourceReference(
            sourceReferenceId: $"source-parent-{caseId}",
            identity: parentExecutable.Identity,
            activationId: $"publication-parent-{caseId}",
            slotId: $"slot-parent-{caseId}");
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(parentExecutable);
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(parentReference);

        var authority = new WorkflowExecutionAuthoritySnapshot("parent-caller", "root-initiator");
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: parentExecutable.Identity.ArtifactId,
                requestedBy: authority.SystemIdentity,
                workflowExecutionId: parentWorkflowExecutionId,
                idempotencyKey: $"start:{parentWorkflowExecutionId}",
                metadata: null,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.BackgroundWeaverRun,
                sourceSelection: new WorkflowExecutableSourceSelection(parentReference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: "correlation-parent",
                // Tenant and partition agree, as an operator alteration (RetryFaultedActivityAsync) requires.
                tenantId: "tenant-42",
                partition: new WorkflowExecutionPartition("tenant-42"),
                authority: authority,
                startAuthority: null,
                dispatchNestingDepth: 0));
    }

    /// <summary>Sweeps until no outbox item is delivered, so every started child and every parent resume has run.</summary>
    internal async ValueTask SweepUntilQuietAsync(int maxSweeps = 200)
    {
        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            var result = await SweepAsync();
            if (result.OutboxAttemptedCount == 0)
                return;
        }

        throw new InvalidOperationException($"The runtime did not go quiet within {maxSweeps} sweeps.");
    }

    private static WorkflowExecutable NewBulkParentExecutable(
        string caseId,
        JsonElement itemsJson,
        bool waitForCompletion,
        bool completedSlot,
        bool faultedSlot,
        IncidentStrategyReference? incidentStrategy,
        bool withDispatch)
    {
        var dispatchInputs = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WorkflowDefinitionId), ChildIdentity.DefinitionId, typeof(string)),
            [nameof(DispatchWorkflowActivity.Inputs)] = VariableBinding(
                nameof(DispatchWorkflowActivity.Inputs), typeof(IReadOnlyDictionary<string, JsonElement>),
                BulkDispatchWorkflowsConstants.CurrentItemVariableName),
            [nameof(DispatchWorkflowActivity.WaitForCompletion)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WaitForCompletion), waitForCompletion, typeof(bool))
        };
        var dispatch = NewNode(
            executableNodeId: BulkDispatchNodeId,
            activityType: typeof(DispatchWorkflowActivity),
            inputBindings: dispatchInputs,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(
                    new DispatchWorkflowPin(ChildIdentity, WorkflowExecutableSourceProvenance.From(ChildSourceReference())),
                    SerializerOptions)
            });

        var slots = new List<ExecutableChildSlot>();
        if (withDispatch)
            slots.Add(new ExecutableChildSlot(BulkDispatchWorkflowsConstants.DispatchSlotName, [dispatch]));
        if (completedSlot)
            slots.Add(new ExecutableChildSlot(BulkDispatchWorkflowsConstants.ChildCompletedSlotName, [SlotProbe(CompletedSlotNodeId, "completed")]));
        if (faultedSlot)
            slots.Add(new ExecutableChildSlot(BulkDispatchWorkflowsConstants.ChildFaultedSlotName, [SlotProbe(FaultedSlotNodeId, "faulted")]));

        var root = NewNode(
            executableNodeId: BulkNodeId,
            activityType: typeof(BulkDispatchWorkflowsActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
            {
                [nameof(BulkDispatchWorkflowsActivity.Items)] = LiteralBinding(nameof(BulkDispatchWorkflowsActivity.Items), itemsJson, typeof(object))
            },
            // The publisher compiles this from IRuntimeAcceptsRescheduledFaultedChild (ExecutableNodeCompiler).
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.AcceptsRescheduledFaultedChild] = "true" },
            childSlots: slots,
            structure: new ExecutableActivityStructure(
                BulkDispatchWorkflowsConstants.StructureKind,
                BulkDispatchWorkflowsConstants.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BulkDispatchWorkflowsExecutableStructure(
                    withDispatch ? BulkDispatchNodeId : null,
                    completedSlot ? CompletedSlotNodeId : null,
                    faultedSlot ? FaultedSlotNodeId : null), SerializerOptions)));

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                $"artifact-parent-{caseId}",
                $"definition-parent-{caseId}",
                $"version-parent-{caseId}",
                "1.0.0",
                $"sha256:parent-{caseId}"),
            rootActivity: root,
            resumeTargets: NodeScopedResumeTargets(dispatch),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies:
            [
                new WorkflowExecutableDependency(ChildIdentity.ArtifactId, ChildIdentity.ArtifactHash, [dispatch.ExecutableNodeId])
            ],
            incidentStrategy: incidentStrategy ?? IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableNode SlotProbe(string nodeId, string slot) =>
        NewNode(
            executableNodeId: nodeId,
            activityType: typeof(BulkSlotProbeActivity),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
            {
                [nameof(BulkSlotProbeActivity.Slot)] = LiteralBinding(nameof(BulkSlotProbeActivity.Slot), slot, typeof(string)),
                [nameof(BulkSlotProbeActivity.Index)] = VariableBinding(
                    nameof(BulkSlotProbeActivity.Index), typeof(int), BulkDispatchWorkflowsConstants.CurrentIndexVariableName),
                [nameof(BulkSlotProbeActivity.Item)] = VariableBinding(
                    nameof(BulkSlotProbeActivity.Item), typeof(IReadOnlyDictionary<string, JsonElement>), BulkDispatchWorkflowsConstants.CurrentItemVariableName)
            });

    private static RuntimeInputBinding VariableBinding(string name, Type type, string variableKey) =>
        new(name, ValueType(type), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(variableKey, BulkNodeId));

    /// <summary>A slot activity that records which slot ran for which item.</summary>
    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    internal sealed class BulkSlotProbeActivity(ChildExecutionProbe probe) : Activity<ActivityUnit>
    {
        [ActivityInput]
        [Required]
        public string Slot { get; set; } = null!;

        [ActivityInput]
        [Required]
        public int Index { get; set; }

        [ActivityInput]
        public IReadOnlyDictionary<string, JsonElement>? Item { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            var message = Item is { } item && item.TryGetValue("message", out var value) ? value.GetString() : null;
            if (message == FaultingSlotMessage && !probe.SlotFaultRepaired)
                throw new InvalidOperationException("The slot probe was asked to fault.");
            probe.Record(context.WorkflowExecutionId, context.InvocationId, new Dictionary<string, object?>
            {
                ["slot"] = Slot,
                ["index"] = Index,
                ["message"] = message
            });
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
    }
}
