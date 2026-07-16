using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for a named runtime checkpoint boundary.
/// </summary>
public sealed class RuntimeCheckpointCommandPayload
{
    public const string WorkflowStartReason = "WorkflowStart";
    public const string ActivityCompletionPropagationReason = "ActivityCompletionPropagation";

    public RuntimeCheckpointCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string checkpointName,
        IReadOnlyCollection<string>? activityExecutionIds,
        string reason,
        IReadOnlyCollection<RuntimePostCommitIntent>? postCommitIntents = null,
        IReadOnlyDictionary<string, JsonElement>? seedVariables = null,
        IReadOnlyDictionary<string, JsonElement>? seedInputs = null,
        JsonElement? seedStimulusInput = null,
        string? seedTriggerNodeId = null,
        WorkflowRunKind runKind = WorkflowRunKind.Unknown,
        WorkflowExecutableSourceProvenance? pinnedSource = null)
        : this(
            pinnedExecutable,
            checkpointName,
            activityExecutionIds,
            reason,
            postCommitIntents,
            seedVariables,
            seedInputs,
            seedStimulusInput,
            seedTriggerNodeId,
            runKind,
            pinnedSource,
            null,
            null,
            null,
            null,
            null)
    {
    }

    public RuntimeCheckpointCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string checkpointName,
        IReadOnlyCollection<string>? activityExecutionIds,
        string reason,
        IReadOnlyCollection<RuntimePostCommitIntent>? postCommitIntents,
        IReadOnlyDictionary<string, JsonElement>? seedVariables,
        IReadOnlyDictionary<string, JsonElement>? seedInputs,
        JsonElement? seedStimulusInput,
        string? seedTriggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceProvenance? pinnedSource,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority)
        : this(
            pinnedExecutable,
            checkpointName,
            activityExecutionIds,
            reason,
            postCommitIntents,
            seedVariables,
            seedInputs,
            seedStimulusInput,
            seedTriggerNodeId,
            runKind,
            pinnedSource,
            parentWorkflowExecutionId,
            correlationId,
            tenantId,
            partition,
            authority,
            dispatchNestingDepth: 0)
    {
    }

    [JsonConstructor]
    public RuntimeCheckpointCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string checkpointName,
        IReadOnlyCollection<string>? activityExecutionIds,
        string reason,
        IReadOnlyCollection<RuntimePostCommitIntent>? postCommitIntents,
        IReadOnlyDictionary<string, JsonElement>? seedVariables,
        IReadOnlyDictionary<string, JsonElement>? seedInputs,
        JsonElement? seedStimulusInput,
        string? seedTriggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceProvenance? pinnedSource,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        int dispatchNestingDepth)
    {
        if (pinnedExecutable is null)
            throw new RuntimeCheckpointCommandPayloadValidationException("Pinned executable cannot be null.", nameof(pinnedExecutable));

        if (string.IsNullOrWhiteSpace(checkpointName))
            throw new RuntimeCheckpointCommandPayloadValidationException("Checkpoint name cannot be blank.", nameof(checkpointName));

        if (string.IsNullOrWhiteSpace(reason))
            throw new RuntimeCheckpointCommandPayloadValidationException("Reason cannot be blank.", nameof(reason));
        ValidateOptional(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        ValidateOptional(correlationId, nameof(correlationId));
        ValidateOptional(tenantId, nameof(tenantId));
        if (dispatchNestingDepth < 0)
            throw new RuntimeCheckpointCommandPayloadValidationException("Dispatch nesting depth cannot be negative.", nameof(dispatchNestingDepth));

        var activityExecutionIdSnapshot = (activityExecutionIds ?? []).ToArray();
        if (activityExecutionIdSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new RuntimeCheckpointCommandPayloadValidationException("Activity execution IDs cannot contain blank values.", nameof(activityExecutionIds));

        if (activityExecutionIdSnapshot.Distinct(StringComparer.Ordinal).Count() != activityExecutionIdSnapshot.Length)
            throw new RuntimeCheckpointCommandPayloadValidationException("Activity execution IDs cannot contain duplicates.", nameof(activityExecutionIds));

        var postCommitIntentSnapshot = (postCommitIntents ?? []).ToArray();
        if (postCommitIntentSnapshot.Any(intent => intent is null))
            throw new RuntimeCheckpointCommandPayloadValidationException("Post-commit intents cannot contain null values.", nameof(postCommitIntents));

        if (postCommitIntentSnapshot
                .Select(intent => intent.IntentId)
                .Distinct(StringComparer.Ordinal)
                .Count() != postCommitIntentSnapshot.Length)
            throw new RuntimeCheckpointCommandPayloadValidationException("Post-commit intent IDs cannot contain duplicates.", nameof(postCommitIntents));

        PinnedExecutable = pinnedExecutable;
        CheckpointName = checkpointName;
        ActivityExecutionIds = activityExecutionIdSnapshot;
        Reason = reason;
        PostCommitIntents = postCommitIntentSnapshot;
        SeedVariables = SnapshotElements(seedVariables);
        SeedInputs = SnapshotElements(seedInputs);
        SeedStimulusInput = seedStimulusInput?.Clone();
        SeedTriggerNodeId = seedTriggerNodeId;
        RunKind = runKind;
        PinnedSource = pinnedSource;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        CorrelationId = correlationId;
        TenantId = tenantId;
        Partition = partition;
        Authority = authority;
        DispatchNestingDepth = dispatchNestingDepth;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string CheckpointName { get; }
    public IReadOnlyCollection<string> ActivityExecutionIds { get; }
    public string Reason { get; }
    public IReadOnlyCollection<RuntimePostCommitIntent> PostCommitIntents { get; }

    /// <summary>
    /// Workflow variable values (name → JSON-encoded value) to persist as durable runtime state when this
    /// checkpoint commits. Populated only for the workflow-started checkpoint; empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> SeedVariables { get; }

    /// <summary>
    /// Workflow input values (name → JSON-encoded value) to persist as durable runtime state when this
    /// checkpoint commits. Populated only for the workflow-started checkpoint; empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> SeedInputs { get; }

    /// <summary>
    /// The start stimulus payload (spec 089 A) to persist on its reserved durable channel when this checkpoint
    /// commits. Populated only for the workflow-started checkpoint of a stimulus-triggered start; null otherwise.
    /// </summary>
    public JsonElement? SeedStimulusInput { get; }

    /// <summary>
    /// The trigger-node identity (spec 089 D) to persist on its reserved durable channel when this checkpoint
    /// commits. Populated only for the workflow-started checkpoint of a trigger-matched start; null otherwise.
    /// </summary>
    public string? SeedTriggerNodeId { get; }

    /// <summary>
    /// The durable execution classification carried from the start command into the first checkpoint.
    /// Legacy checkpoint payloads without this field use <see cref="WorkflowRunKind.Unknown"/>.
    /// </summary>
    public WorkflowRunKind RunKind { get; }

    /// <summary>
    /// Immutable source attribution carried by the workflow-started checkpoint. Null for legacy payloads.
    /// </summary>
    public WorkflowExecutableSourceProvenance? PinnedSource { get; }
    public string? ParentWorkflowExecutionId { get; }
    public string? CorrelationId { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition? Partition { get; }
    public WorkflowExecutionAuthoritySnapshot? Authority { get; }

    /// <summary>Durable cross-workflow dispatch depth. Missing legacy values default to zero.</summary>
    public int DispatchNestingDepth { get; }

    private static IReadOnlyDictionary<string, JsonElement> SnapshotElements(IReadOnlyDictionary<string, JsonElement>? values) =>
        (values ?? new Dictionary<string, JsonElement>()).ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new RuntimeCheckpointCommandPayloadValidationException("Optional checkpoint context values cannot be blank.", parameterName);
    }
}

public sealed class RuntimeCheckpointCommandPayloadValidationException(string message, string? paramName)
    : ArgumentException(message, paramName);
