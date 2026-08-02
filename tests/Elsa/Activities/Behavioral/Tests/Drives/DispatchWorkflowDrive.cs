using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// DispatchWorkflow: all five declared outcomes and both declared outputs, plus the enforcement of its one
/// required input. The richest outcome set in the library, and — until this drive — the one with no evidence
/// that any of it was reachable.
/// </summary>
/// <remarks>
/// <para>
/// Every wait-mode outcome is a mirror of the child's fate, so each is reached by making a real child
/// <em>actually meet that fate</em> — complete, fault, get cancelled, or become unstartable — and letting the
/// completion enricher, the post-commit outbox and the parent-resume path do the rest. Nothing hand-feeds the
/// parent a resume payload: a drive that synthesised the payload would prove the <c>switch</c> in
/// <c>ResumeAsync</c> compiles, not that the runtime can ever deliver that status.
/// </para>
/// <para>
/// This is why the activity needs the published-start path rather than the plain harness run: it reads its
/// parent execution's durable partition and authority snapshots, which only a real start dispatch seeds, and it
/// requires the exact child-executable pin the publish compiler stamps into node metadata.
/// </para>
/// </remarks>
public sealed class DispatchWorkflowDrive : IActivityDrive
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>What the dispatched child does, which is what decides the parent's wait-mode outcome.</summary>
    private enum ChildBehaviour
    {
        /// <summary>Runs a probe leaf and completes.</summary>
        Completes,

        /// <summary>Faults during execution, ending the child workflow Faulted.</summary>
        Faults,

        /// <summary>Waits on an event so the child is still live and can be cancelled.</summary>
        Waits
    }

    public Type ActivityType => typeof(DispatchWorkflowActivity);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // Fire-and-forget completes inline: the child execution id is known before the child exists, and there is
        // no result to carry, which is why this case populates only one of the two declared outputs.
        await DriveOutcomeAsync(recorder, "dispatched", ChildBehaviour.Completes, waitForCompletion: false);

        await DriveOutcomeAsync(recorder, "completed", ChildBehaviour.Completes, waitForCompletion: true);
        await DriveOutcomeAsync(recorder, "faulted", ChildBehaviour.Faults, waitForCompletion: true);
        await DriveOutcomeAsync(recorder, "cancelled", ChildBehaviour.Waits, waitForCompletion: true, cancelChild: true);
        await DriveOutcomeAsync(
            recorder,
            "dispatch-failed",
            ChildBehaviour.Completes,
            waitForCompletion: true,
            retireChildPublication: true);

        await DriveMissingWorkflowDefinitionIdAsync(recorder);
    }

    private static async Task DriveOutcomeAsync(
        ActivityDriveRecorder recorder,
        string caseId,
        ChildBehaviour childBehaviour,
        bool waitForCompletion,
        bool cancelChild = false,
        bool retireChildPublication = false)
    {
        // DispatchFailed is the exhaustion of child-start delivery, so that case caps delivery at a single attempt.
        // The alternative — leaving the default four and letting the backoff run — measures the same terminal state
        // via a fake clock, which proves less and hides more.
        await using var harness = NewHarness(caseId, childStartMaxDeliveryAttempts: retireChildPublication ? 1 : 4);
        var parentWorkflowExecutionId = $"wfexec-parent-{caseId}";
        var childReference = await harness.PublishAsync(NewChildExecutable(caseId, childBehaviour), $"source-child-{caseId}");
        var parentExecutable = NewParentExecutable(caseId, childReference, waitForCompletion, bindDefinitionId: true);
        var parentReference = await harness.PublishAsync(parentExecutable, $"source-parent-{caseId}");

        await harness.StartPublishedAsync(parentReference, parentWorkflowExecutionId, correlationId: $"correlation-{caseId}");

        // Retiring after the parent has staged its dispatch is what makes the child unstartable: the pin still
        // names a real artifact, but the live Published reference the child start is gated on is gone.
        if (retireChildPublication)
            await harness.RetirePublicationAsync(childReference, "unpublished");

        if (cancelChild)
        {
            // One sweep admits the child, which then waits. Cancelling it is the control-plane act that gives the
            // parent something Cancelled to observe; the sweeps after it carry that terminal status back.
            await harness.SweepAsync();
            await harness.CancelAsync(await ChildWorkflowExecutionIdAsync(harness, parentWorkflowExecutionId));
        }

        await harness.SweepUntilQuietAsync();

        recorder.Record(typeof(DispatchWorkflowActivity), await harness.ReadRunAsync(parentWorkflowExecutionId), DispatchNodeId);
    }

    /// <summary>
    /// The required-input case (#1119 F3): omitting <c>WorkflowDefinitionId</c> is caught by <c>VF-ACT-004</c> on
    /// the start command, before the activity is ever constructed — the start comes back carrying the guard's
    /// refusal rather than the activity faulting mid-run. Looking only for a committed activity fault would
    /// conclude, wrongly, that nothing enforces it.
    /// </summary>
    private static async Task DriveMissingWorkflowDefinitionIdAsync(ActivityDriveRecorder recorder)
    {
        const string caseId = "missing-definition-id";
        await using var harness = NewHarness(caseId);
        var childReference = await harness.PublishAsync(
            NewChildExecutable(caseId, ChildBehaviour.Completes),
            $"source-child-{caseId}");
        var parentReference = await harness.PublishAsync(
            NewParentExecutable(caseId, childReference, waitForCompletion: false, bindDefinitionId: false),
            $"source-parent-{caseId}");

        try
        {
            await harness.StartPublishedAsync(parentReference, $"wfexec-parent-{caseId}");
            recorder.RecordRequiredInputFault(
                typeof(DispatchWorkflowActivity),
                nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                await harness.ReadRunAsync($"wfexec-parent-{caseId}"),
                DispatchNodeId);
        }
        catch (InvalidOperationException rejection)
        {
            recorder.RecordRequiredInputFault(
                typeof(DispatchWorkflowActivity),
                nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                run: null,
                DispatchNodeId,
                rejection);
        }
    }

    private const string DispatchNodeId = "node-dispatch";
    private const string ChildNodeId = "node-child";

    private static WorkflowExecutionHarness NewHarness(string caseId, int childStartMaxDeliveryAttempts = 4) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature
            {
                ChildStartMaxDeliveryAttempts = childStartMaxDeliveryAttempts
            }.ConfigureServices(services))
            .Build(Enumerable.Range(1, 16).Select(ordinal => $"actexec-{caseId}-{ordinal}"));

    // The child's execution id is derived from the parent's execution id and the dispatch activity's execution
    // id. Both are read back out of committed state rather than assumed from the ids this drive handed the
    // harness, so cancelling the child cannot silently target an execution the run never created.
    private static async Task<string> ChildWorkflowExecutionIdAsync(
        WorkflowExecutionHarness harness,
        string parentWorkflowExecutionId)
    {
        var run = await harness.ReadRunAsync(parentWorkflowExecutionId);
        var activityExecutionId = run.State(DispatchNodeId).Execution.ActivityExecutionId;
        return new WorkflowDispatchIdentity(parentWorkflowExecutionId, activityExecutionId).ChildWorkflowExecutionId;
    }

    private static WorkflowExecutable NewParentExecutable(
        string caseId,
        WorkflowExecutableSourceReference childReference,
        bool waitForCompletion,
        bool bindDefinitionId)
    {
        var childIdentity = NewIdentity($"child-{caseId}");
        var inputs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(DispatchWorkflowActivity.WaitForCompletion)] = waitForCompletion
        };

        // Left unbound entirely for the requiredness case, which is what an author who never filled the field in
        // would publish — not a blank string the activity's own body would reject.
        if (bindDefinitionId)
            inputs[nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = childIdentity.DefinitionId;

        var pin = new DispatchWorkflowPin(childIdentity, WorkflowExecutableSourceProvenance.From(childReference));
        var node = Nodes.Leaf(
            DispatchNodeId,
            typeof(DispatchWorkflowActivity),
            inputs,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(pin, SerializerOptions)
            });

        return new WorkflowExecutable(
            identity: NewIdentity($"parent-{caseId}"),
            rootActivity: node,
            resumeTargets: NodeScopedCompletionResumeTarget(node),
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewChildExecutable(string caseId, ChildBehaviour behaviour) =>
        new(
            identity: NewIdentity($"child-{caseId}"),
            rootActivity: NewChildRoot(behaviour),
            resumeTargets: ChildResumeTargets(behaviour),
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            // DispatchWorkflow refuses a child that carries no supported input contract, so an empty declaration
            // is the minimum a dispatchable workflow must publish.
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode NewChildRoot(ChildBehaviour behaviour) => behaviour switch
    {
        ChildBehaviour.Completes => WorkflowExecutionHarness.NewProbeNode(ChildNodeId),
        ChildBehaviour.Faults => WorkflowExecutionHarness.NewFaultingNode(ChildNodeId),
        ChildBehaviour.Waits => Nodes.Leaf(
            ChildNodeId,
            typeof(Event),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(Event.EventName)] = "child-waits",
                [nameof(Event.CanStartWorkflow)] = false
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(behaviour), behaviour, null)
    };

    /// <summary>A waiting child registers a bookmark, which the executable must declare a resume target for.</summary>
    private static Dictionary<string, WorkflowExecutableResumeTarget> ChildResumeTargets(ChildBehaviour behaviour)
    {
        if (behaviour != ChildBehaviour.Waits)
            return new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);

        var scopedId = WorkflowExecutableResumeTarget.ComposeScopedId(ChildNodeId, Event.ResumeTargetId);
        return new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            [scopedId] = new(scopedId, ChildNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId)
        };
    }

    /// <summary>
    /// The resume-target map the publish compiler emits for a waiting dispatch node: node-scoped key with the
    /// activity-local id preserved, so resolution takes the production (node, local id) path.
    /// </summary>
    private static Dictionary<string, WorkflowExecutableResumeTarget> NodeScopedCompletionResumeTarget(ExecutableNode node)
    {
        var scopedId = WorkflowExecutableResumeTarget.ComposeScopedId(
            node.ExecutableNodeId,
            DispatchWorkflowConstants.CompletionResumeTargetId);
        return new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [scopedId] = new(
                scopedId,
                node.ExecutableNodeId,
                "OnChildCompletedAsync",
                new Dictionary<string, string>(),
                DispatchWorkflowConstants.CompletionResumeTargetId)
        };
    }

    private static WorkflowExecutableIdentity NewIdentity(string key) =>
        new($"artifact-{key}", $"definition-{key}", $"version-{key}", "1.0.0", $"sha256:{key}");
}
