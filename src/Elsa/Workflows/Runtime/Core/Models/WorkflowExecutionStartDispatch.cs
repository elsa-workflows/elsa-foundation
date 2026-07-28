using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowExecutionStartDispatchRequest
{
    public WorkflowExecutionStartDispatchRequest(
        string artifactId,
        string requestedBy,
        string? workflowExecutionId = null,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        IReadOnlyDictionary<string, object?>? inputs = null,
        JsonElement? stimulusInput = null,
        string? triggerNodeId = null,
        WorkflowRunKind runKind = WorkflowRunKind.Unknown,
        WorkflowExecutableSourceSelection? sourceSelection = null,
        WorkflowExecutableProvenanceRequirement provenanceRequirement = WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
        : this(
            artifactId,
            requestedBy,
            workflowExecutionId,
            idempotencyKey,
            metadata,
            variables,
            inputs,
            stimulusInput,
            triggerNodeId,
            runKind,
            sourceSelection,
            provenanceRequirement,
            null,
            null,
            null,
            null,
            null,
            triggerMetadata)
    {
    }

    public WorkflowExecutionStartDispatchRequest(
        string artifactId,
        string requestedBy,
        string? workflowExecutionId,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, object?>? inputs,
        JsonElement? stimulusInput,
        string? triggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceSelection? sourceSelection,
        WorkflowExecutableProvenanceRequirement provenanceRequirement,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
        : this(
            artifactId,
            requestedBy,
            workflowExecutionId,
            idempotencyKey,
            metadata,
            variables,
            inputs,
            stimulusInput,
            triggerNodeId,
            runKind,
            sourceSelection,
            provenanceRequirement,
            parentWorkflowExecutionId,
            correlationId,
            tenantId,
            partition,
            authority,
            null,
            triggerMetadata)
    {
    }

    public WorkflowExecutionStartDispatchRequest(
        string artifactId,
        string requestedBy,
        string? workflowExecutionId,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, object?>? inputs,
        JsonElement? stimulusInput,
        string? triggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceSelection? sourceSelection,
        WorkflowExecutableProvenanceRequirement provenanceRequirement,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        WorkflowExecutableStartAuthority? startAuthority,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
        : this(
            artifactId,
            requestedBy,
            workflowExecutionId,
            idempotencyKey,
            metadata,
            variables,
            inputs,
            stimulusInput,
            triggerNodeId,
            runKind,
            sourceSelection,
            provenanceRequirement,
            parentWorkflowExecutionId,
            correlationId,
            tenantId,
            partition,
            authority,
            startAuthority,
            dispatchNestingDepth: 0,
            testScope: null,
            triggerMetadata: triggerMetadata)
    {
    }

    [JsonConstructor]
    public WorkflowExecutionStartDispatchRequest(
        string artifactId,
        string requestedBy,
        string? workflowExecutionId,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, object?>? inputs,
        JsonElement? stimulusInput,
        string? triggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceSelection? sourceSelection,
        WorkflowExecutableProvenanceRequirement provenanceRequirement,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        WorkflowExecutableStartAuthority? startAuthority,
        int dispatchNestingDepth,
        WorkflowTestScope? testScope = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Workflow execution ID cannot be blank when provided.", nameof(workflowExecutionId));

        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key cannot be blank when provided.", nameof(idempotencyKey));

        if (triggerNodeId is not null && string.IsNullOrWhiteSpace(triggerNodeId))
            throw new ArgumentException("Trigger node ID cannot be blank when provided.", nameof(triggerNodeId));
        ValidateOptional(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        ValidateOptional(correlationId, nameof(correlationId));
        ValidateOptional(tenantId, nameof(tenantId));
        ValidateStartAuthority(startAuthority, sourceSelection, provenanceRequirement);
        ValidateDispatchNestingDepth(dispatchNestingDepth);
        ValidateTestScope(runKind, parentWorkflowExecutionId, tenantId, partition, testScope);

        authority ??= WorkflowExecutionAuthoritySnapshot.CreateRoot(requestedBy);
        if (!StringComparer.Ordinal.Equals(requestedBy, authority.SystemIdentity))
            throw new ArgumentException("RequestedBy must match the execution authority system identity.", nameof(authority));

        ArtifactId = artifactId;
        WorkflowExecutionId = workflowExecutionId;
        IdempotencyKey = idempotencyKey;
        RequestedBy = requestedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        Variables = SnapshotValues(variables);
        Inputs = SnapshotValues(inputs);
        StimulusInput = stimulusInput?.Clone();
        TriggerNodeId = triggerNodeId;
        TriggerMetadata = RuntimeModelMetadata.Snapshot(triggerMetadata);
        RunKind = runKind;
        SourceSelection = sourceSelection;
        ProvenanceRequirement = provenanceRequirement;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        CorrelationId = correlationId;
        TenantId = tenantId;
        Partition = partition;
        Authority = authority;
        StartAuthority = startAuthority;
        DispatchNestingDepth = dispatchNestingDepth;
        TestScope = testScope;
    }

    public string ArtifactId { get; }
    public string? WorkflowExecutionId { get; }
    public string? IdempotencyKey { get; }
    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Initial workflow variable values keyed by variable name, seeded as durable runtime state at start so
    /// later activities resolve <c>variables.*</c> input expressions. Empty when none are supplied.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }

    /// <summary>
    /// Workflow input values keyed by input name, seeded as durable runtime state at start so later activities
    /// resolve <c>input.*</c> input expressions. Empty when none are supplied.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Inputs { get; }

    /// <summary>
    /// The stimulus payload that triggered this start (spec 089 A), seeded as durable runtime state on its own
    /// reserved channel — the start-side counterpart of the resume path's
    /// <c>BookmarkResumeDispatchRequest.Input</c>. Deliberately NOT part of <see cref="Inputs"/>: it never
    /// shares the workflow-input namespace, so it cannot collide with author-declared inputs and cannot be
    /// forged through caller-facing input bags. Null for non-stimulus starts.
    /// </summary>
    public JsonElement? StimulusInput { get; }

    /// <summary>
    /// The executable node id of the matched trigger binding that started this run (spec 089 D), carried on its
    /// own reserved channel — the start-side counterpart of the resume path's node identity. Lets a mid-flow-capable
    /// activity (e.g. <c>HttpEndpoint</c>) tell whether it is the node that triggered this run. Deliberately NOT part
    /// of <see cref="Inputs"/>: it never shares the workflow-input namespace, so it cannot collide with author-declared
    /// inputs and cannot be forged through caller-facing input bags. Null for direct (non-trigger) starts.
    /// </summary>
    public string? TriggerNodeId { get; }

    /// <summary>
    /// The matched trigger binding's free-form metadata map (spec 117 D4), carried verbatim from
    /// <c>WorkflowTriggerBinding.Metadata</c> on its own reserved channel — never part of <see cref="Inputs"/>.
    /// Seeded durably at start so a structural trigger activity (e.g. <c>BpmnProcess</c>) can read per-descriptor
    /// routing facets (such as the BPMN start element id). Empty for direct starts and metadata-less bindings.
    /// </summary>
    public IReadOnlyDictionary<string, string> TriggerMetadata { get; }

    /// <summary>
    /// The explicit durable classification to pin to this execution. Background Weaver callers use
    /// <see cref="WorkflowRunKind.BackgroundWeaverRun"/> through this typed start-dispatch API.
    /// </summary>
    public WorkflowRunKind RunKind { get; }

    /// <summary>
    /// Server-validated selector for the authoritative live source reference. The selector only disambiguates
    /// references; none of its values are copied into execution provenance until the dispatcher has matched them
    /// against the requested artifact, required scope and current liveness.
    /// </summary>
    public WorkflowExecutableSourceSelection? SourceSelection { get; }

    /// <summary>
    /// Controls whether a stored artifact without any source-reference history may use the internal seeded/direct
    /// compatibility path. Public execution surfaces require authoritative live provenance; internal callers that
    /// seed content-addressed artifacts directly retain the legacy null-provenance behavior explicitly.
    /// </summary>
    public WorkflowExecutableProvenanceRequirement ProvenanceRequirement { get; }
    public string? ParentWorkflowExecutionId { get; }
    public string? CorrelationId { get; }
    public string? TenantId { get; }

    /// <summary>Explicit partition for deferred/global delivery; null retains ambient-root compatibility.</summary>
    public WorkflowExecutionPartition? Partition { get; }

    /// <summary>Typed runtime-owned authority and root-initiator attribution.</summary>
    public WorkflowExecutionAuthoritySnapshot Authority { get; }

    /// <summary>
    /// Optional typed authority for selecting an executable. Null preserves the legacy live-reference or
    /// reference-less compatibility paths represented by <see cref="SourceSelection"/> and
    /// <see cref="ProvenanceRequirement"/>. Internal dispatch may instead carry exact retained-dependency
    /// provenance; public execution endpoints must not accept that authority from callers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutableStartAuthority? StartAuthority { get; }

    /// <summary>Cross-workflow dispatch depth. Root and legacy requests default to zero.</summary>
    public int DispatchNestingDepth { get; }

    /// <summary>Authoritative finite test-run scope. Null for non-test and legacy starts.</summary>
    public WorkflowTestScope? TestScope { get; }

    private static IReadOnlyDictionary<string, object?> SnapshotValues(IReadOnlyDictionary<string, object?>? values) =>
        (values ?? new Dictionary<string, object?>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static void ValidateStartAuthority(
        WorkflowExecutableStartAuthority? startAuthority,
        WorkflowExecutableSourceSelection? sourceSelection,
        WorkflowExecutableProvenanceRequirement provenanceRequirement)
    {
        if (startAuthority is null)
            return;

        if (startAuthority.Kind == WorkflowExecutableStartAuthorityKind.RetainedDependency)
        {
            if (sourceSelection is not null || provenanceRequirement != WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy)
                throw new ArgumentException("Retained-dependency authority cannot be combined with live-source selection or requirements.", nameof(startAuthority));
            return;
        }

        if (sourceSelection is null || provenanceRequirement != WorkflowExecutableProvenanceRequirement.RequireLiveReference)
            throw new ArgumentException("Explicit live-reference authority requires a live source selection and provenance requirement.", nameof(startAuthority));
    }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Optional workflow start context values cannot be blank.", parameterName);
    }

    private static void ValidateDispatchNestingDepth(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Dispatch nesting depth cannot be negative.");
    }

    private static void ValidateTestScope(
        WorkflowRunKind runKind,
        string? parentWorkflowExecutionId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowTestScope? testScope)
    {
        if (testScope is null)
        {
            if (runKind == WorkflowRunKind.TestRun && parentWorkflowExecutionId is null)
                throw new ArgumentException("A new root TestRun start requires an authoritative workflow test scope.", nameof(testScope));
            return;
        }
        if (runKind != WorkflowRunKind.TestRun)
            throw new ArgumentException("A workflow test scope requires TestRun run kind.", nameof(testScope));
        if (!StringComparer.Ordinal.Equals(tenantId, testScope.TenantId))
            throw new ArgumentException("The workflow test scope tenant must match the start tenant.", nameof(testScope));
        if (partition is not null && !Equals(partition, testScope.Partition))
            throw new ArgumentException("The workflow test scope partition must match the start partition.", nameof(testScope));
    }
}

public enum WorkflowExecutableProvenanceRequirement
{
    AllowReferenceLessLegacy,
    RequireLiveReference
}

/// <summary>Identifies the authoritative route by which an executable may be selected for a new start.</summary>
public enum WorkflowExecutableStartAuthorityKind
{
    LiveReference = 0,
    RetainedDependency = 1
}

/// <summary>
/// Typed executable-selection authority. A live-reference start continues to use the existing validated source
/// selection fields. A retained-dependency start is authorized only by the exact immutable parent artifact edge
/// identified by <see cref="RetainedDependency"/>.
/// </summary>
public sealed class WorkflowExecutableStartAuthority
{
    [JsonConstructor]
    public WorkflowExecutableStartAuthority(
        WorkflowExecutableStartAuthorityKind kind,
        WorkflowExecutableRetainedDependencyAuthority? retainedDependency = null)
    {
        if (kind == WorkflowExecutableStartAuthorityKind.LiveReference && retainedDependency is not null)
            throw new ArgumentException("Live-reference start authority cannot carry retained-dependency provenance.", nameof(retainedDependency));
        if (kind == WorkflowExecutableStartAuthorityKind.RetainedDependency && retainedDependency is null)
            throw new ArgumentException("Retained-dependency start authority requires exact parent provenance.", nameof(retainedDependency));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown executable start authority kind.");

        Kind = kind;
        RetainedDependency = retainedDependency;
    }

    public WorkflowExecutableStartAuthorityKind Kind { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutableRetainedDependencyAuthority? RetainedDependency { get; }

    public static WorkflowExecutableStartAuthority LiveReference { get; } = new(WorkflowExecutableStartAuthorityKind.LiveReference);

    public static WorkflowExecutableStartAuthority FromRetainedDependency(
        string parentArtifactId,
        string parentArtifactHash,
        string dispatchNodeId) =>
        new(
            WorkflowExecutableStartAuthorityKind.RetainedDependency,
            new WorkflowExecutableRetainedDependencyAuthority(parentArtifactId, parentArtifactHash, dispatchNodeId));
}

/// <summary>
/// Immutable provenance for a child start authorized by one exact dispatch edge on a retained parent artifact.
/// The requested child must match the artifact ID and full hash bound to this dispatch node on the loaded parent.
/// </summary>
public sealed class WorkflowExecutableRetainedDependencyAuthority
{
    [JsonConstructor]
    public WorkflowExecutableRetainedDependencyAuthority(
        string parentArtifactId,
        string parentArtifactHash,
        string dispatchNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchNodeId);

        ParentArtifactId = parentArtifactId;
        ParentArtifactHash = parentArtifactHash;
        DispatchNodeId = dispatchNodeId;
    }

    public string ParentArtifactId { get; }
    public string ParentArtifactHash { get; }
    public string DispatchNodeId { get; }
}

/// <summary>
/// Selects an authoritative source reference without allowing callers to assert definition or version facts.
/// Direct execution uses <see cref="SourceReferenceId"/>; stimulus routing uses the publication/slot identity
/// already stamped on the server-owned trigger binding.
/// </summary>
public sealed record WorkflowExecutableSourceSelection
{
    public WorkflowExecutableSourceSelection(
        string? sourceReferenceId = null,
        string? publicationId = null,
        string? slotId = null)
    {
        if (sourceReferenceId is not null && string.IsNullOrWhiteSpace(sourceReferenceId))
            throw new ArgumentException("Source reference ID cannot be blank when provided.", nameof(sourceReferenceId));
        if (publicationId is not null && string.IsNullOrWhiteSpace(publicationId))
            throw new ArgumentException("Publication ID cannot be blank when provided.", nameof(publicationId));
        if (slotId is not null && string.IsNullOrWhiteSpace(slotId))
            throw new ArgumentException("Slot ID cannot be blank when provided.", nameof(slotId));
        if (sourceReferenceId is null && publicationId is null && slotId is null)
            throw new ArgumentException("A source reference, publication or slot ID is required.");
        if (sourceReferenceId is not null && (publicationId is not null || slotId is not null))
            throw new ArgumentException("Select by source reference ID or publication identity, not both.");

        SourceReferenceId = sourceReferenceId;
        PublicationId = publicationId;
        SlotId = slotId;
    }

    public string? SourceReferenceId { get; }
    public string? PublicationId { get; }
    public string? SlotId { get; }
}

public sealed class WorkflowExecutionStartCommandPayload
{
    public WorkflowExecutionStartCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string requestedArtifactId,
        IReadOnlyDictionary<string, JsonElement>? variables = null,
        IReadOnlyDictionary<string, JsonElement>? inputs = null,
        JsonElement? stimulusInput = null,
        string? triggerNodeId = null,
        WorkflowRunKind runKind = WorkflowRunKind.Unknown,
        WorkflowExecutableSourceProvenance? pinnedSource = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
        : this(
            pinnedExecutable,
            requestedArtifactId,
            variables,
            inputs,
            stimulusInput,
            triggerNodeId,
            runKind,
            pinnedSource,
            null,
            null,
            null,
            null,
            null,
            triggerMetadata)
    {
    }

    public WorkflowExecutionStartCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string requestedArtifactId,
        IReadOnlyDictionary<string, JsonElement>? variables,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        JsonElement? stimulusInput,
        string? triggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceProvenance? pinnedSource,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
        : this(
            pinnedExecutable,
            requestedArtifactId,
            variables,
            inputs,
            stimulusInput,
            triggerNodeId,
            runKind,
            pinnedSource,
            parentWorkflowExecutionId,
            correlationId,
            tenantId,
            partition,
            authority,
            null,
            triggerMetadata)
    {
    }

    public WorkflowExecutionStartCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string requestedArtifactId,
        IReadOnlyDictionary<string, JsonElement>? variables,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        JsonElement? stimulusInput,
        string? triggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceProvenance? pinnedSource,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        WorkflowExecutableStartAuthority? startAuthority,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
        : this(
            pinnedExecutable,
            requestedArtifactId,
            variables,
            inputs,
            stimulusInput,
            triggerNodeId,
            runKind,
            pinnedSource,
            parentWorkflowExecutionId,
            correlationId,
            tenantId,
            partition,
            authority,
            startAuthority,
            dispatchNestingDepth: 0,
            testScope: null,
            triggerMetadata: triggerMetadata)
    {
    }

    [JsonConstructor]
    public WorkflowExecutionStartCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string requestedArtifactId,
        IReadOnlyDictionary<string, JsonElement>? variables,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        JsonElement? stimulusInput,
        string? triggerNodeId,
        WorkflowRunKind runKind,
        WorkflowExecutableSourceProvenance? pinnedSource,
        string? parentWorkflowExecutionId,
        string? correlationId,
        string? tenantId,
        WorkflowExecutionPartition? partition,
        WorkflowExecutionAuthoritySnapshot? authority,
        WorkflowExecutableStartAuthority? startAuthority,
        int dispatchNestingDepth,
        WorkflowTestScope? testScope = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedArtifactId);
        ValidateOptional(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        ValidateOptional(correlationId, nameof(correlationId));
        ValidateOptional(tenantId, nameof(tenantId));
        ValidateStartAuthority(startAuthority, pinnedSource);
        if (dispatchNestingDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(dispatchNestingDepth), dispatchNestingDepth, "Dispatch nesting depth cannot be negative.");
        if (testScope is not null)
        {
            if (runKind != WorkflowRunKind.TestRun)
                throw new ArgumentException("A workflow test scope requires TestRun run kind.", nameof(testScope));
            if (!StringComparer.Ordinal.Equals(tenantId, testScope.TenantId))
                throw new ArgumentException("The workflow test scope tenant must match the command tenant.", nameof(testScope));
            if (partition is not null && !Equals(partition, testScope.Partition))
                throw new ArgumentException("The workflow test scope partition must match the command partition.", nameof(testScope));
        }

        PinnedExecutable = pinnedExecutable;
        RequestedArtifactId = requestedArtifactId;
        Variables = SnapshotElements(variables);
        Inputs = SnapshotElements(inputs);
        StimulusInput = stimulusInput?.Clone();
        TriggerNodeId = triggerNodeId;
        TriggerMetadata = RuntimeModelMetadata.Snapshot(triggerMetadata);
        RunKind = runKind;
        PinnedSource = pinnedSource;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        CorrelationId = correlationId;
        TenantId = tenantId;
        Partition = partition;
        Authority = authority;
        StartAuthority = startAuthority;
        DispatchNestingDepth = dispatchNestingDepth;
        TestScope = testScope;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string RequestedArtifactId { get; }

    /// <summary>
    /// Initial workflow variable values (name → JSON-encoded value) carried from start dispatch so the
    /// workflow-started checkpoint can seed them as durable runtime state.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Variables { get; }

    /// <summary>
    /// Workflow input values (name → JSON-encoded value) carried from start dispatch so the workflow-started
    /// checkpoint can seed them as durable runtime state.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Inputs { get; }

    /// <summary>
    /// The stimulus payload (spec 089 A) carried from start dispatch so the workflow-started checkpoint can
    /// seed it on its reserved durable channel, separate from <see cref="Inputs"/>. Null for non-stimulus starts.
    /// </summary>
    public JsonElement? StimulusInput { get; }

    /// <summary>
    /// The trigger-node identity (spec 089 D) carried from start dispatch so the workflow-started checkpoint can
    /// seed it on its reserved durable channel, separate from <see cref="Inputs"/>. Null for direct (non-trigger)
    /// starts.
    /// </summary>
    public string? TriggerNodeId { get; }

    /// <summary>
    /// The matched trigger binding's metadata map (spec 117 D4) carried from start dispatch so the
    /// workflow-started checkpoint can seed it on its reserved durable channel, separate from <see cref="Inputs"/>.
    /// Empty for direct starts and metadata-less bindings.
    /// </summary>
    public IReadOnlyDictionary<string, string> TriggerMetadata { get; }

    /// <summary>
    /// The run classification captured at dispatch. Missing values in legacy serialized commands use
    /// <see cref="WorkflowRunKind.Unknown"/>.
    /// </summary>
    public WorkflowRunKind RunKind { get; }

    /// <summary>
    /// Server-validated source attribution selected at dispatch. Null for legacy commands and direct execution
    /// of seeded artifacts that have no source references.
    /// </summary>
    public WorkflowExecutableSourceProvenance? PinnedSource { get; }
    public string? ParentWorkflowExecutionId { get; }
    public string? CorrelationId { get; }
    public string? TenantId { get; }
    public WorkflowExecutionPartition? Partition { get; }
    public WorkflowExecutionAuthoritySnapshot? Authority { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowExecutableStartAuthority? StartAuthority { get; }

    /// <summary>Durable cross-workflow dispatch depth. Missing legacy values default to zero.</summary>
    public int DispatchNestingDepth { get; }

    /// <summary>Authoritative finite test-run scope. Null for non-test and legacy commands.</summary>
    public WorkflowTestScope? TestScope { get; }

    public static IReadOnlyDictionary<string, JsonElement> ToJsonValues(IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(
            item => item.Key,
            item => item.Value is JsonElement json ? json.Clone() : JsonSerializer.SerializeToElement(item.Value, item.Value?.GetType() ?? typeof(object)),
            StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, JsonElement> SnapshotElements(IReadOnlyDictionary<string, JsonElement>? values) =>
        (values ?? new Dictionary<string, JsonElement>()).ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);

    private static void ValidateStartAuthority(
        WorkflowExecutableStartAuthority? startAuthority,
        WorkflowExecutableSourceProvenance? pinnedSource)
    {
        if (startAuthority is null)
            return;

        if (startAuthority.Kind == WorkflowExecutableStartAuthorityKind.RetainedDependency)
        {
            if (pinnedSource is not null)
                throw new ArgumentException("Retained-dependency authority cannot carry historical child source provenance.", nameof(startAuthority));
            return;
        }

        if (pinnedSource is null)
            throw new ArgumentException("Explicit live-reference authority requires validated source provenance.", nameof(startAuthority));
    }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Optional workflow start context values cannot be blank.", parameterName);
    }
}

public sealed class WorkflowExecutionStartDispatchResult
{
    public WorkflowExecutionStartDispatchResult(
        string workflowExecutionId,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutionCommandDispatchResult commandDispatch,
        WorkflowExecutionActorDescriptor agent,
        WorkflowExecutableSourceProvenance? pinnedSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentNullException.ThrowIfNull(commandDispatch);
        ArgumentNullException.ThrowIfNull(agent);

        if (!string.Equals(workflowExecutionId, commandDispatch.WorkflowExecutionId, StringComparison.Ordinal))
            throw new ArgumentException("Start dispatch result workflow execution ID must match command dispatch result.", nameof(commandDispatch));

        if (!string.Equals(workflowExecutionId, agent.WorkflowExecutionId, StringComparison.Ordinal))
            throw new ArgumentException("Start dispatch result workflow execution ID must match agent descriptor.", nameof(agent));

        WorkflowExecutionId = workflowExecutionId;
        PinnedExecutable = pinnedExecutable;
        CommandDispatch = commandDispatch;
        Agent = agent;
        PinnedSource = pinnedSource;
    }

    public string WorkflowExecutionId { get; }
    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public WorkflowExecutionCommandDispatchResult CommandDispatch { get; }
    public WorkflowExecutionActorDescriptor Agent { get; }
    public WorkflowExecutableSourceProvenance? PinnedSource { get; }
}
