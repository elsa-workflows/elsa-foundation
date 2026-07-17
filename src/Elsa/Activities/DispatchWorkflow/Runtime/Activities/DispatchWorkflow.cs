using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Activities;

/// <summary>Dispatches an exactly pinned Published workflow executable as a child execution.</summary>
[
    ActivityOutcome(DispatchWorkflowOutcomes.Dispatched),
    ActivityOutcome(DispatchWorkflowOutcomes.Completed),
    ActivityOutcome(DispatchWorkflowOutcomes.Faulted),
    ActivityOutcome(DispatchWorkflowOutcomes.Cancelled),
    ActivityOutcome(DispatchWorkflowOutcomes.DispatchFailed)
]
public sealed class DispatchWorkflow(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IWorkflowExecutableInputValidator inputValidator,
    IWorkflowDispatchStager dispatchStager,
    IOptions<DispatchWorkflowOptions> options,
    TimeProvider timeProvider) :
    StatefulActivity<DispatchWorkflowActivityResult, DispatchWorkflowState, WorkflowDispatchParentResumePayload>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public const string ActivityType = DispatchWorkflowConstants.ActivityType;

    [ActivityInput(
        Key = nameof(WorkflowDefinitionId),
        Order = 0,
        UIHint = ActivityInputUIHints.Dropdown,
        OptionsProvider = DispatchWorkflowConstants.WorkflowDefinitionOptionsKey)]
    [Required]
    public string WorkflowDefinitionId { get; set; } = null!;

    [ActivityInput(Key = nameof(Inputs), Order = 10)]
    public IReadOnlyDictionary<string, JsonElement>? Inputs { get; set; }

    [ActivityInput(Key = nameof(WaitForCompletion), Order = 20, DefaultValue = "false", DefaultSyntax = "Literal")]
    public bool WaitForCompletion { get; set; }

    [ActivityInput(Key = nameof(CancelChildOnParentCancellation), Order = 30, DefaultValue = "true", DefaultSyntax = "Literal")]
    public bool CancelChildOnParentCancellation { get; set; } = true;

    [ActivityInput(Key = nameof(CorrelationId), Order = 40)]
    public string? CorrelationId { get; set; }

    protected override async ValueTask<ActivityTransition<DispatchWorkflowActivityResult, DispatchWorkflowState>> ExecuteAsync(
        ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(WorkflowDefinitionId))
            throw new InvalidOperationException("DispatchWorkflow requires a nonblank WorkflowDefinitionId.");

        var activityState = await activityExecutionStateStore.FindAsync(
            context.WorkflowExecutionId,
            context.InvocationId,
            context.CancellationToken)
            ?? throw new InvalidOperationException(
                $"DispatchWorkflow activity invocation '{context.InvocationId}' was not found.");
        if (!StringComparer.Ordinal.Equals(activityState.Execution.ExecutableNodeId, context.ExecutableNodeId))
            throw new InvalidOperationException("DispatchWorkflow activity invocation does not match its executable node.");

        var parent = await workflowExecutionStateStore.FindAsync(context.WorkflowExecutionId, context.CancellationToken)
            ?? throw new InvalidOperationException($"DispatchWorkflow parent execution '{context.WorkflowExecutionId}' was not found.");
        var parentExecutable = await executableStore.FindAsync(parent.PinnedExecutable.ArtifactId, context.CancellationToken)
            ?? throw new InvalidOperationException("DispatchWorkflow parent executable is no longer available.");
        if (!StringComparer.Ordinal.Equals(parentExecutable.Identity.ArtifactHash, parent.PinnedExecutable.ArtifactHash))
            throw new InvalidOperationException("DispatchWorkflow parent executable identity is inconsistent.");
        if (!parentExecutable.NodesById.TryGetValue(context.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException("DispatchWorkflow executable node is no longer available.");
        if (!executableNode.Metadata.TryGetValue(DispatchWorkflowConstants.PinnedTargetMetadataKey, out var serializedPin))
            throw new InvalidOperationException("DispatchWorkflow requires an exact child executable pin created during parent publication.");

        var pin = JsonSerializer.Deserialize<DispatchWorkflowPin>(serializedPin, SerializerOptions)
            ?? throw new InvalidOperationException("DispatchWorkflow child executable pin is invalid.");
        ValidatePin(pin, WorkflowDefinitionId.Trim());

        var childExecutable = await executableStore.FindAsync(pin.Executable.ArtifactId, context.CancellationToken)
            ?? throw new InvalidOperationException("DispatchWorkflow pinned child executable is no longer available.");
        if (!StringComparer.Ordinal.Equals(childExecutable.Identity.ArtifactHash, pin.Executable.ArtifactHash))
            throw new InvalidOperationException("DispatchWorkflow pinned child executable identity is inconsistent.");
        var inputContract = childExecutable.InputContract;
        if (inputContract is null || inputContract.Version != WorkflowExecutableInputContract.CurrentVersion)
            throw new InvalidOperationException("DispatchWorkflow pinned child executable does not carry a supported input contract.");

        var maxNestingDepth = options.Value.MaxNestingDepth;
        DispatchWorkflowOptions.ValidateMaxNestingDepth(maxNestingDepth, nameof(DispatchWorkflowOptions.MaxNestingDepth));
        int childNestingDepth;
        try
        {
            childNestingDepth = checked(parent.DispatchNestingDepth + 1);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("DispatchWorkflow nesting depth is invalid.", exception);
        }
        if (childNestingDepth > maxNestingDepth)
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow child nesting depth {childNestingDepth} exceeds the configured maximum of {maxNestingDepth}.");
        }
        var partition = parent.Partition
            ?? throw new InvalidOperationException("DispatchWorkflow requires the parent execution's durable partition snapshot.");
        var parentAuthority = parent.Authority
            ?? throw new InvalidOperationException("DispatchWorkflow requires the parent execution's durable authority snapshot.");
        var childAuthority = new WorkflowExecutionAuthoritySnapshot(
            context.WorkflowExecutionId,
            parentAuthority.RootInitiator,
            parentAuthority.Metadata);
        var correlationId = string.IsNullOrWhiteSpace(CorrelationId)
            ? parent.CorrelationId
            : CorrelationId.Trim();
        var inputs = Inputs ?? new Dictionary<string, JsonElement>();
        var inputValidation = inputValidator.Validate(
            inputContract,
            inputs);
        if (!inputValidation.IsValid)
        {
            throw new InvalidOperationException(
                "DispatchWorkflow inputs are invalid: " +
                string.Join(" ", inputValidation.Findings.Select(finding => finding.Message)));
        }
        var jsonInputs = inputValidation.NormalizedInputs;
        var identity = new WorkflowDispatchIdentity(context.WorkflowExecutionId, context.InvocationId);
        // Wait mode needs the durable invoke event time for replay stability. Fire-and-forget retains its
        // established wall-clock timestamp contract.
        var now = WaitForCompletion
            ? activityState.Attempts?.SingleOrDefault(attempt => StringComparer.Ordinal.Equals(attempt.AttemptId, context.AttemptId))?.StartedAt
              ?? throw new InvalidOperationException("DispatchWorkflow wait mode requires a durable activity attempt timestamp.")
            : timeProvider.GetUtcNow();
        var provenance = pin.Source
            ?? throw new InvalidOperationException("DispatchWorkflow child executable pin does not carry source provenance.");
        var dispatchNodeId = context.ExecutableNodeId;
        var hasRetainedEdge = parentExecutable.Dependencies.Any(dependency =>
                StringComparer.Ordinal.Equals(dependency.ArtifactId, pin.Executable.ArtifactId) &&
                StringComparer.Ordinal.Equals(dependency.ArtifactHash, pin.Executable.ArtifactHash) &&
                dependency.DispatchNodeIds.Contains(dispatchNodeId, StringComparer.Ordinal));
        var dispatchMode = WaitForCompletion
            ? WorkflowDispatchMode.WaitForCompletion
            : WorkflowDispatchMode.FireAndForget;
        var dispatchMetadata = new Dictionary<string, string>
        {
            ["runtime.sourceReferenceId"] = provenance.SourceReferenceId
        };
        WorkflowDispatchLifecycle.SetEffectiveCancellationPolicy(
            dispatchMetadata,
            dispatchMode,
            CancelChildOnParentCancellation);
        var record = new WorkflowDispatchRecord(
            dispatchId: identity.DispatchId,
            parentWorkflowExecutionId: context.WorkflowExecutionId,
            parentActivityExecutionId: context.InvocationId,
            childWorkflowExecutionId: identity.ChildWorkflowExecutionId,
            childExecutable: pin.Executable,
            childSource: provenance,
            mode: dispatchMode,
            status: WorkflowDispatchStatus.Pending,
            correlationId: correlationId,
            tenantId: parent.TenantId,
            partition: partition,
            runKind: parent.RunKind,
            authority: childAuthority,
            inputDescriptors: jsonInputs.Select(item => new WorkflowDispatchInputDescriptor(item.Key, DescribeType(item.Value))).ToArray(),
            createdAt: now,
            updatedAt: now,
            metadata: dispatchMetadata,
            dispatchNestingDepth: childNestingDepth,
            testScope: parent.TestScope);
        var startPayload = new WorkflowDispatchStartPayload(
            identity.DispatchId,
            context.WorkflowExecutionId,
            context.InvocationId,
            identity.ChildWorkflowExecutionId,
            pin.Executable,
            hasRetainedEdge ? null : provenance,
            jsonInputs,
            correlationId,
            parent.TenantId,
            partition,
            parent.RunKind,
            childAuthority,
            hasRetainedEdge ? parent.PinnedExecutable : null,
            hasRetainedEdge ? dispatchNodeId : null,
            childNestingDepth,
            parent.TestScope);
        var startIntent = new RuntimePostCommitIntent(
            intentId: identity.StartIntentId,
            workflowExecutionId: context.WorkflowExecutionId,
            kind: DispatchWorkflowConstants.StartChildIntentKind,
            recordedAt: now,
            activityExecutionId: context.InvocationId,
            idempotencyKey: identity.StartIdempotencyKey,
            payload: JsonSerializer.SerializeToElement(startPayload, SerializerOptions),
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = identity.DispatchId });

        var waitBookmark = WaitForCompletion
            ? new ActivityBookmarkRequest(
                bookmarkId: identity.WaitBookmarkId,
                resumeTargetId: DispatchWorkflowConstants.CompletionResumeTargetId,
                stimulusType: DispatchWorkflowConstants.WaitStimulusType,
                stimulusHash: identity.WaitStimulusHash,
                expiresAt: null,
                metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = identity.DispatchId })
            : null;

        dispatchStager.StageWorkflowDispatch(waitBookmark is null
            ? new WorkflowDispatchCheckpointRequest(record, startIntent)
            : new WorkflowDispatchCheckpointRequest(
                record,
                startIntent,
                waitBookmark,
                DispatchWorkflowConstants.CompletionResumeTargetId,
                DispatchWorkflowConstants.WaitStimulusType));
        if (!WaitForCompletion)
        {
            return Complete(
                new DispatchWorkflowActivityResult(identity.ChildWorkflowExecutionId, Result: null),
                DispatchWorkflowOutcomes.Dispatched);
        }

        var registration = new ActivityTriggerRegistration<WorkflowDispatchParentResumePayload>(
            DispatchWorkflowConstants.CompletionResumeTargetId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = identity.DispatchId });
        return Suspend(new DispatchWorkflowState(identity.ChildWorkflowExecutionId), [registration]);
    }

    [ResumeTarget(DispatchWorkflowConstants.CompletionResumeTargetId)]
    protected override ValueTask<ActivityTransition<DispatchWorkflowActivityResult, DispatchWorkflowState>> ResumeAsync(
        ActivityResumeContext<DispatchWorkflowState, WorkflowDispatchParentResumePayload> context)
    {
        var payload = context.Trigger;
        var identity = new WorkflowDispatchIdentity(context.WorkflowExecutionId, context.InvocationId);
        if (!StringComparer.Ordinal.Equals(context.State.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(context.RegistrationId, identity.WaitBookmarkId) ||
            !StringComparer.Ordinal.Equals(payload.ParentWorkflowExecutionId, context.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.ParentActivityExecutionId, context.InvocationId) ||
            !StringComparer.Ordinal.Equals(payload.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.BookmarkId, identity.WaitBookmarkId) ||
            !StringComparer.Ordinal.Equals(payload.StimulusType, DispatchWorkflowConstants.WaitStimulusType) ||
            !StringComparer.Ordinal.Equals(payload.StimulusHash, identity.WaitStimulusHash) ||
            !DispatchWorkflowResult.SupportsParentResume(payload.Result.Status))
        {
            throw new InvalidOperationException("DispatchWorkflow completion resume payload does not match the current activity execution.");
        }

        var outcome = payload.Result.Status switch
        {
            WorkflowDispatchStatus.Completed => DispatchWorkflowOutcomes.Completed,
            WorkflowDispatchStatus.Faulted => DispatchWorkflowOutcomes.Faulted,
            WorkflowDispatchStatus.Cancelled => DispatchWorkflowOutcomes.Cancelled,
            WorkflowDispatchStatus.DispatchFailed => DispatchWorkflowOutcomes.DispatchFailed,
            _ => throw new InvalidOperationException("DispatchWorkflow completion resume payload has an unsupported child terminal status.")
        };
        return ValueTask.FromResult(Complete(
            new DispatchWorkflowActivityResult(payload.ChildWorkflowExecutionId, payload.Result),
            outcome));
    }

    private static void ValidatePin(DispatchWorkflowPin pin, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(pin.Executable);
        ArgumentNullException.ThrowIfNull(pin.Source);
        if (!StringComparer.Ordinal.Equals(pin.Source.DefinitionId, definitionId))
            throw new InvalidOperationException("DispatchWorkflow child executable pin does not match the authored workflow definition.");
    }

    private static string DescribeType(object? value) => value switch
    {
        null => "null",
        JsonElement json => $"json:{json.ValueKind}",
        _ => value.GetType().FullName ?? value.GetType().Name
    };
}
