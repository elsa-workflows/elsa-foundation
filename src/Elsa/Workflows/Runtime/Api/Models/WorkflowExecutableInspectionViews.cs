using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public enum WorkflowExecutableListScope
{
    Published,
    TestRuns,
    All
}

/// <summary>Describes one source reference that keeps a workflow executable reachable.</summary>
/// <remarks>
/// <see cref="SourceKind"/> is the canonical source discriminator. <see cref="SourceType"/> is emitted only as
/// a Runtime API v1 compatibility alias and always contains the same value; new clients must use
/// <see cref="SourceKind"/>. The alias is scheduled for removal with Runtime API v2.
/// </remarks>
[method: JsonConstructor]
public sealed record ExecutableSourceReferenceView(
    string SourceReferenceId,
    string ArtifactId,
    string Scope,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string? PublicationId,
    string? SlotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DeletedAt,
    string? DeletedReason,
    bool Live)
{
    /// <summary>Runtime API v1 compatibility alias for <see cref="SourceKind"/>.</summary>
    [Obsolete("sourceType is a compatibility alias for sourceKind in Runtime API v1 and will be removed in Runtime API v2. Use sourceKind.")]
    [JsonPropertyName("sourceType")]
    public string? SourceType
    {
        get => SourceKind;
        init => SourceKind = ResolveSourceKind(value, SourceKind);
    }

    /// <summary>Runtime API v1 constructor retained for source and binary compatibility.</summary>
    [Obsolete("The sourceType constructor parameter is retained for Runtime API v1 compatibility and will be removed in Runtime API v2. Use the sourceKind constructor overload.")]
    public ExecutableSourceReferenceView(
        string SourceReferenceId,
        string ArtifactId,
        string Scope,
        string? SourceType,
        string? SourceKind,
        string? SourceId,
        string? SourceVersion,
        string DefinitionId,
        string DefinitionVersionId,
        string ArtifactVersion,
        string? PublicationId,
        string? SlotId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? PublishedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? DeletedAt,
        string? DeletedReason,
        bool Live)
        : this(
            SourceReferenceId,
            ArtifactId,
            Scope,
            ResolveSourceKind(SourceType, SourceKind),
            SourceId,
            SourceVersion,
            DefinitionId,
            DefinitionVersionId,
            ArtifactVersion,
            PublicationId,
            SlotId,
            CreatedAt,
            PublishedAt,
            ExpiresAt,
            DeletedAt,
            DeletedReason,
            Live)
    {
    }

    /// <summary>Runtime API v1 deconstructor retained for source and binary compatibility.</summary>
    [Obsolete("The sourceType deconstruction output is retained for Runtime API v1 compatibility and will be removed in Runtime API v2. Use the sourceKind deconstruction overload.")]
    public void Deconstruct(
        out string SourceReferenceId,
        out string ArtifactId,
        out string Scope,
        out string? SourceType,
        out string? SourceKind,
        out string? SourceId,
        out string? SourceVersion,
        out string DefinitionId,
        out string DefinitionVersionId,
        out string ArtifactVersion,
        out string? PublicationId,
        out string? SlotId,
        out DateTimeOffset CreatedAt,
        out DateTimeOffset? PublishedAt,
        out DateTimeOffset? ExpiresAt,
        out DateTimeOffset? DeletedAt,
        out string? DeletedReason,
        out bool Live)
    {
        SourceReferenceId = this.SourceReferenceId;
        ArtifactId = this.ArtifactId;
        Scope = this.Scope;
        SourceType = this.SourceKind;
        SourceKind = this.SourceKind;
        SourceId = this.SourceId;
        SourceVersion = this.SourceVersion;
        DefinitionId = this.DefinitionId;
        DefinitionVersionId = this.DefinitionVersionId;
        ArtifactVersion = this.ArtifactVersion;
        PublicationId = this.PublicationId;
        SlotId = this.SlotId;
        CreatedAt = this.CreatedAt;
        PublishedAt = this.PublishedAt;
        ExpiresAt = this.ExpiresAt;
        DeletedAt = this.DeletedAt;
        DeletedReason = this.DeletedReason;
        Live = this.Live;
    }

    public static ExecutableSourceReferenceView From(WorkflowExecutableSourceReference reference, DateTimeOffset now) =>
        new(
            reference.SourceReferenceId,
            reference.ArtifactId,
            reference.Scope.ToString(),
            reference.SourceKind,
            reference.SourceId,
            reference.SourceVersion,
            reference.DefinitionId,
            reference.DefinitionVersionId,
            reference.ArtifactVersion,
            reference.PublicationId,
            reference.SlotId,
            reference.CreatedAt,
            reference.PublishedAt,
            reference.ExpiresAt,
            reference.DeletedAt,
            reference.DeletedReason,
            reference.IsLive(now));

    private static string? ResolveSourceKind(string? sourceType, string? sourceKind)
    {
        if (sourceType is not null && sourceKind is not null && !string.Equals(sourceType, sourceKind, StringComparison.Ordinal))
            throw new ArgumentException("sourceType and sourceKind must contain the same value.", nameof(sourceType));

        return sourceKind ?? sourceType;
    }
}

public sealed record WorkflowExecutableSummaryView(
    string ArtifactId,
    string ArtifactVersion,
    string ArtifactHash,
    string DefinitionId,
    string DefinitionVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? DeletedAt,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount,
    int LiveSourceReferenceCount,
    int RetainedExecutionCount,
    IReadOnlyCollection<ExecutableSourceReferenceView> References);

public sealed record WorkflowExecutablesListView(IReadOnlyCollection<WorkflowExecutableSummaryView> Items);

public sealed record WorkflowExecutableNodeView(
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string? StructureKind,
    IReadOnlyCollection<WorkflowExecutableInputBindingView> InputBindings,
    IReadOnlyCollection<WorkflowExecutableChildSlotView> ChildSlots,
    IReadOnlyCollection<WorkflowExecutableConnectionView> Connections);

public sealed record WorkflowExecutableConnectionEndpointView(string NodeId, string? Port);

public sealed record WorkflowExecutableConnectionView(
    WorkflowExecutableConnectionEndpointView Source,
    WorkflowExecutableConnectionEndpointView Target);

public sealed record WorkflowExecutableInputBindingView(
    string InputName,
    string Source,
    string? Summary,
    string? InputKey = null,
    bool IsSensitive = false,
    JsonElement? LiteralValue = null,
    RuntimeExpressionBinding? Expression = null,
    RuntimeWorkflowRequestReference? WorkflowRequest = null,
    RuntimeVariableReference? Variable = null,
    RuntimeActivityResultReference? ActivityResult = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record WorkflowExecutableAuthoredInputView(
    string ExecutableNodeId,
    string InputKey,
    string? ExpressionType,
    JsonElement? Value,
    bool IsSensitive,
    string AccessState);

public sealed record WorkflowExecutableCompiledInputView(
    string ExecutableNodeId,
    WorkflowExecutableInputBindingView Binding,
    string AccessState);

public sealed record WorkflowExecutableInputSourcesView(
    string ArtifactId,
    string SourceReferenceId,
    string AccessState,
    IReadOnlyCollection<WorkflowExecutableAuthoredInputView> AuthoredInputs,
    IReadOnlyCollection<WorkflowExecutableCompiledInputView> CompiledInputs);

public sealed record WorkflowExecutableChildSlotView(string Name, IReadOnlyCollection<WorkflowExecutableNodeView> Activities);

public sealed record WorkflowExecutableInputContractView(
    int Version,
    IReadOnlyCollection<WorkflowExecutableDeclaredInputView> Inputs);

public sealed record WorkflowExecutableDeclaredInputView(
    string Name,
    TypeReference Type,
    bool IsRequired,
    JsonElement? DefaultValue);

public sealed record WorkflowExecutableDependencyView(
    string ArtifactId,
    string ArtifactHash,
    IReadOnlyCollection<string> DispatchNodeIds);

public sealed record WorkflowExecutableDetailsView(
    string ArtifactId,
    string ArtifactHash,
    DateTimeOffset CreatedAt,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount,
    int LiveSourceReferenceCount,
    int RetainedExecutionCount,
    WorkflowExecutableNodeView RootActivity,
    IReadOnlyDictionary<string, string> Metadata,
    WorkflowExecutableChosenReferenceView? ChosenReference,
    IReadOnlyCollection<ExecutableSourceReferenceView> References)
{
    /// <summary>The immutable versioned workflow-input contract, or <see langword="null"/> for a legacy artifact.</summary>
    public WorkflowExecutableInputContractView? InputContract { get; init; }

    /// <summary>Canonical immutable direct executable dependencies; publication/source facts are excluded.</summary>
    public IReadOnlyCollection<WorkflowExecutableDependencyView> Dependencies { get; init; } = [];
}

public sealed record WorkflowExecutableChosenReferenceView(
    string SourceReferenceId,
    string Selection,
    IReadOnlyCollection<WorkflowExecutableLayoutRecord> Layout);

public sealed record ExecutableProvenanceView(
    string ArtifactId,
    IReadOnlyCollection<ExecutableSourceReferenceView> SourceReferences,
    int RetainedExecutionCount,
    bool ProtectedFromCollection);
