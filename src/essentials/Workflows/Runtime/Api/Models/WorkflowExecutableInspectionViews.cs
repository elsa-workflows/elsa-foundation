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
/// <remarks><see cref="SourceKind"/> is the canonical source discriminator.</remarks>
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
            reference.ActivationId,
            reference.SlotId,
            reference.CreatedAt,
            reference.PublishedAt,
            reference.ExpiresAt,
            reference.DeletedAt,
            reference.DeletedReason,
            reference.IsLive(now));
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
    IReadOnlyCollection<WorkflowExecutableConnectionView> Connections,
    IReadOnlyCollection<WorkflowExecutableOutputCaptureView>? OutputCaptures = null,
    WorkflowExecutableBpmnStructureView? BpmnStructure = null);

/// <summary>Allowlisted semantic projection for supported persisted BPMN executable structures.</summary>
public sealed record WorkflowExecutableBpmnStructureView(
    IReadOnlyCollection<WorkflowExecutableBpmnElementView> Elements,
    IReadOnlyCollection<WorkflowExecutableBpmnSequenceFlowView> SequenceFlows);

public sealed record WorkflowExecutableBpmnElementView(
    string ElementId,
    string ElementType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChildNodeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);

public sealed record WorkflowExecutableBpmnSequenceFlowView(
    string FlowId,
    string SourceRef,
    string TargetRef,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConditionOutcome = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsDefault = null);

public sealed record WorkflowExecutableConnectionEndpointView(string NodeId, string? Port);

public sealed record WorkflowExecutableConnectionView(
    WorkflowExecutableConnectionEndpointView Source,
    WorkflowExecutableConnectionEndpointView Target);

/// <summary>One compiled input binding of an executable node.</summary>
/// <remarks>
/// <see cref="IsSensitive"/> is true when the binding's effective policy marks its value sensitive or as requiring
/// encryption; the inspector then leaves <see cref="Summary"/> and every source detail out. <see cref="Secret"/> carries
/// a secret read's reference (name, type and scope), which is not a value; for a secret read the inspector also uses the
/// reference name as <see cref="Summary"/>.
/// </remarks>
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
    ValueConversionPlan? ConversionPlan = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    RuntimeSecretReference? Secret = null);

public sealed record WorkflowExecutableOutputCaptureView(
    string OutputName,
    string ValueId,
    RuntimeValueTypeDescriptor Type,
    string Lifecycle,
    string Storage,
    string StorageDriverKey,
    bool CaptureOnSuccessfulCompletion,
    ValueConversionPlan? ConversionPlan,
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
    IReadOnlyCollection<WorkflowExecutableLayoutRecord> Layout)
{
    public IReadOnlyCollection<WorkflowExecutableActivityPresentationRecord> ActivityPresentation { get; init; } = [];
}

public sealed record ExecutableProvenanceView(
    string ArtifactId,
    IReadOnlyCollection<ExecutableSourceReferenceView> SourceReferences,
    int RetainedExecutionCount,
    bool ProtectedFromCollection);
