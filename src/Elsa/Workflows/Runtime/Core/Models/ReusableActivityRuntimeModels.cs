using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable, content-addressed Runtime material for one reusable activity version.</summary>
public sealed class ExecutableActivityTemplate
{
    public ExecutableActivityTemplate(
        string templateId,
        string templateHash,
        ExecutableNode root,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        IReadOnlyCollection<ExecutableActivityTemplateDependency> directDependencies,
        IReadOnlyCollection<ExecutableActivityTemplateIdentity> closedTemplates,
        IReadOnlyCollection<RuntimeRequirement> runtimeRequirements,
        string providerFingerprint,
        IReadOnlyDictionary<string, string> compatibilityMetadata,
        DateTimeOffset createdAt,
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageDriverRequirements = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(resumeTargets);
        ArgumentNullException.ThrowIfNull(directDependencies);
        ArgumentNullException.ThrowIfNull(closedTemplates);
        ArgumentNullException.ThrowIfNull(runtimeRequirements);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerFingerprint);
        ArgumentNullException.ThrowIfNull(compatibilityMetadata);

        var nodes = Flatten(root).ToArray();
        TemplateId = templateId;
        TemplateHash = templateHash;
        Root = root;
        NodesById = new ReadOnlyDictionary<string, ExecutableNode>(nodes.ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal));
        ResumeTargets = new ReadOnlyDictionary<string, WorkflowExecutableResumeTarget>(resumeTargets.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        DirectDependencies = Array.AsReadOnly(directDependencies.ToArray());
        ClosedTemplates = Array.AsReadOnly(closedTemplates.ToArray());
        RuntimeRequirements = Array.AsReadOnly(runtimeRequirements
            .Concat(nodes.Select(node => new RuntimeRequirement(node.Descriptor.ConsumerKey, node.Descriptor.SchemaVersion)))
            .Distinct()
            .OrderBy(item => item.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(item => item.SchemaVersion, StringComparer.Ordinal)
            .ToArray());
        StorageDriverRequirements = Array.AsReadOnly((storageDriverRequirements ?? [])
            .Concat(nodes.SelectMany(node => node.OutputCaptures.Values)
                .Select(capture => new RuntimeStorageDriverRequirement(capture.StorageDriverKey)))
            .Distinct()
            .OrderBy(item => item.DriverKey, StringComparer.Ordinal)
            .ToArray());
        ProviderFingerprint = providerFingerprint;
        CompatibilityMetadata = new ReadOnlyDictionary<string, string>(compatibilityMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        CreatedAt = createdAt;
    }

    public string TemplateId { get; }
    public string TemplateHash { get; }
    public ExecutableNode Root { get; }
    public IReadOnlyDictionary<string, ExecutableNode> NodesById { get; }
    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets { get; }
    public IReadOnlyCollection<ExecutableActivityTemplateDependency> DirectDependencies { get; }
    public IReadOnlyCollection<ExecutableActivityTemplateIdentity> ClosedTemplates { get; }
    public IReadOnlyCollection<RuntimeRequirement> RuntimeRequirements { get; }
    public IReadOnlyCollection<RuntimeStorageDriverRequirement> StorageDriverRequirements { get; }
    public string ProviderFingerprint { get; }
    public IReadOnlyDictionary<string, string> CompatibilityMetadata { get; }
    public DateTimeOffset CreatedAt { get; }

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities).Reverse())
                stack.Push(child);
        }
    }
}

public sealed record ExecutableActivityTemplateIdentity(string TemplateId, string TemplateHash);

public sealed record ExecutableActivityTemplateDependency(
    string DefinitionId,
    string DefinitionVersionId,
    string Version,
    string TemplateId,
    string TemplateHash,
    string OccurrenceId,
    ActivityInvocationOrigin NodeOrigin,
    string? ParentOccurrenceId = null,
    string ChildSlotName = "activity-graph",
    int ChildIndex = 0);

/// <summary>A readable placement path. Its canonical length-framed bytes produce the placement namespace.</summary>
public sealed record ActivityInvocationOrigin(IReadOnlyList<ActivityInvocationOriginSegment> Segments)
{
    public static ActivityInvocationOrigin Empty { get; } = new([]);

    public byte[] ToCanonicalBytes()
    {
        using var stream = new MemoryStream();
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        foreach (var segment in Segments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segment.Id);
            var identityBytes = Encoding.UTF8.GetBytes(segment.Id);
            stream.WriteByte((byte)segment.Kind);
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, identityBytes.Length);
            stream.Write(lengthBuffer);
            stream.Write(identityBytes);
        }

        return stream.ToArray();
    }

    public string ComputePlacementNamespace() => Convert.ToHexStringLower(SHA256.HashData(ToCanonicalBytes()));
}

public sealed record ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind Kind, string Id);

public enum ActivityInvocationOriginSegmentKind : byte
{
    WorkflowRoot = 1,
    AuthoredNode = 2,
    TemplateBoundary = 3,
    NestedPlacement = 4
}

/// <summary>Boundary-scoped layout kept on a Source Reference and excluded from behavior hashes.</summary>
public sealed record ExecutableLayoutSidecar(IReadOnlyList<ExecutableLayoutBoundarySegment> BoundarySegments)
{
    public static ExecutableLayoutSidecar Empty { get; } = new([]);
}

public sealed record ExecutableLayoutBoundarySegment(
    string BoundarySegmentId,
    ActivityInvocationOrigin BoundaryOrigin,
    string? TemplateHash,
    IReadOnlyList<ExecutableActivityLayoutRecord> Records,
    IReadOnlyList<ActivityInvocationOrigin> NestedBoundaryOrigins);

public sealed record ExecutableActivityLayoutRecord(
    string TemplateNodeId,
    string AuthoredActivityId,
    string ExecutableNodeId,
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    JsonElement? AdditionalProperties = null,
    string? ActivityType = null,
    string? ActivityTypeVersion = null,
    bool HasPinnedGeometry = true);

/// <summary>Runtime facts owned by one ordinary outer activity execution; this is not a child workflow.</summary>
public sealed record ActivityExecutionScope(
    string ExecutionScopeId,
    string WorkflowExecutionId,
    string OuterActivityExecutionId,
    string DefinitionId,
    string DefinitionVersionId,
    string Version,
    string TemplateHash,
    ActivityInvocationOrigin InvocationOrigin,
    IReadOnlySet<ActivityExecutionScopeCapability> Capabilities);

public enum ActivityExecutionScopeCapability
{
    DurableValues,
    Services,
    Tracing,
    Time,
    Cancellation,
    WorkflowRootMutation,
    TriggerEntry
}

public sealed record ActivityExecutionAttemptLineage(
    int AttemptNumber,
    string FirstAttemptActivityExecutionId,
    string? PreviousAttemptActivityExecutionId);

public sealed record ActivityExecutionAttemptNavigation(
    ActivityExecutionAttemptLineage Lineage,
    string? NextAttemptActivityExecutionId,
    int TotalAttempts);

public sealed record ActivityExecutionBoundary(
    string Kind,
    string DefinitionId,
    string DefinitionVersionId,
    string Version,
    string TemplateHash,
    ActivityInvocationOrigin InvocationOrigin,
    string ExecutionScopeId,
    bool HasChildren,
    int DirectChildCount,
    long CommittedDescendantCount,
    ActivityExecutionHierarchyAggregate Aggregate,
    bool LayoutAvailable,
    string? ExecutableNodeId = null);

public sealed record ActivityExecutionHierarchyAggregate(
    ActivityExecutionHierarchyAggregateStatus Status,
    long Total,
    long Scheduled,
    long Running,
    long Suspended,
    long Completed,
    long Faulted,
    long Cancelled,
    long BlockingIncidentCount,
    long RetryCount,
    long LastExecutionSequence);

public enum ActivityExecutionHierarchyAggregateStatus
{
    Empty,
    Running,
    Suspended,
    Completed,
    Faulted,
    Cancelled
}

public sealed record ActivityExecutionHierarchyQuery(
    string WorkflowExecutionId,
    string RootActivityExecutionId,
    string? Cursor,
    int? Limit,
    IReadOnlySet<ActivityExecutionHierarchyInclude> Include,
    string AuthorizationProfile,
    string TenantScope = "global:");

public enum ActivityExecutionHierarchyInclude
{
    Outcomes,
    Bookmarks,
    Incidents
}

public sealed record ActivityExecutionHierarchyRoot(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutionScopeId,
    string DefinitionVersionId,
    string TemplateHash);

public sealed record ActivityExecutionHierarchyPage(
    ActivityExecutionHierarchyRoot Root,
    long CommittedThroughSequence,
    int EffectiveLimit,
    IReadOnlyList<ActivityExecutionHierarchyItem> Items,
    string? NextCursor);

public sealed record ActivityExecutionHierarchyItem(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    ActivityExecutionStatus Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ParentActivityExecutionId,
    string? SchedulingActivityExecutionId,
    int RelativeDepth,
    string? BranchId,
    string? IterationId,
    IReadOnlyCollection<string> OutcomeNames,
    int BookmarkCount,
    int IncidentCount,
    int BlockingIncidentCount,
    ActivityExecutionAttemptLineage? Attempt,
    ActivityExecutionBoundary? Boundary,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Committed relation evidence used by hierarchy adapters to build fixed-watermark pages.</summary>
public sealed record ActivityExecutionHierarchyRecord(
    string WorkflowExecutionId,
    string ExecutionScopeId,
    string ActivityExecutionId,
    string? ParentActivityExecutionId,
    long ExecutionSequence,
    ActivityExecutionHierarchyItem Item);

public sealed record ActivityExecutionHierarchyCursorState(
    string TenantScope,
    string AuthorizationProfile,
    string WorkflowExecutionId,
    string RootActivityExecutionId,
    IReadOnlyList<ActivityExecutionHierarchyInclude> Include,
    int EffectiveLimit,
    long CommittedThroughSequence,
    long LastExecutionSequence,
    string LastActivityExecutionId,
    int SchemaVersion = 1);

public sealed record ActivityExecutionCursorFailureMetadata(
    string CursorClass,
    ActivityExecutionCursorBindingState BoundaryBinding,
    ActivityExecutionCursorBindingState QueryBinding,
    ActivityExecutionCursorBindingState AccessBinding,
    bool Recoverable,
    string RecoveryAction);

public enum ActivityExecutionCursorBindingState
{
    Unknown,
    Matched,
    Mismatched
}

public enum ActivityExecutionHierarchyCursorFailure
{
    Invalid,
    BindingMismatch,
    Expired
}

public sealed class ActivityExecutionHierarchyCursorException(
    ActivityExecutionHierarchyCursorFailure failure,
    string message,
    Exception? innerException = null,
    ActivityExecutionCursorFailureMetadata? metadata = null) : Exception(message, innerException)
{
    public ActivityExecutionHierarchyCursorFailure Failure { get; } = failure;
    public ActivityExecutionCursorFailureMetadata? Metadata { get; } = metadata;
}

public sealed record ActivityExecutionLayout(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ArtifactId,
    string SourceReferenceId,
    ActivityExecutionLayoutSelection Selection,
    ActivityInvocationOrigin BoundaryOrigin,
    string TemplateHash,
    IReadOnlyList<ExecutableActivityLayoutRecord> Nodes,
    IReadOnlyList<ActivityExecutionLayoutConnection> Connections,
    IReadOnlyList<ActivityExecutionNestedBoundaryLayout> NestedBoundaries);

public enum ActivityExecutionLayoutSelection
{
    ExecutedReference,
    Automatic
}

public sealed record ActivityExecutionLayoutConnection(
    ActivityExecutionLayoutEndpoint Source,
    ActivityExecutionLayoutEndpoint Target,
    IReadOnlyList<ActivityExecutionLayoutVertex>? Vertices);

public sealed record ActivityExecutionLayoutEndpoint(string ExecutableNodeId, string? Port);
public sealed record ActivityExecutionLayoutVertex(double X, double Y);
public sealed record ActivityExecutionNestedBoundaryLayout(string ActivityExecutionId, string ExecutableNodeId, string TemplateHash, bool LayoutAvailable);
