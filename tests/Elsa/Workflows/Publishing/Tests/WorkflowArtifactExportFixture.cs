using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;


namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// A publish-capable engine's three stores, wired to the real closure factory.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is a stub. The stores are the production in-memory defaults the publishing feature registers, and
/// artifact identities come from the production <see cref="WorkflowExecutableHasher"/> — so a child's id and hash
/// are genuinely derived from its content and a parent's dependency edge genuinely pins that derived identity. A
/// fixture that hand-wrote ids would let the walk pass on identities no compiler could ever produce, and the
/// diamond test in particular would prove nothing: it is only interesting because two parents independently
/// arrive at the <em>same</em> content-addressed child.
/// </para>
/// <para>
/// The one place identity is supplied by hand is <see cref="SaveCorruptedAsync"/>, which exists to model store
/// states the compiler cannot produce (a contradicted hash, a dependency cycle) and which the factory must still
/// refuse rather than walk forever or ship silently.
/// </para>
/// </remarks>
internal sealed class WorkflowArtifactExportFixture
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly IWorkflowExecutableHasher Hasher = new Elsa.Workflows.Runtime.Services.WorkflowExecutableHasher();

    private int _referenceSequence;

    public InMemoryWorkflowExecutableStore Executables { get; } = new();
    public InMemoryWorkflowExecutableSourceReferenceStore SourceReferences { get; } = new();
    public InMemoryWorkflowTriggerBindingStore TriggerBindings { get; } = new();

    public IWorkflowArtifactClosureFactory CreateFactory() =>
        new WorkflowArtifactClosureFactory(Executables, SourceReferences, TriggerBindings);

    /// <summary>A leaf node in the shape a compiled artifact carries: consumer-keyed descriptor, readable payload.</summary>
    public static ExecutableNode Node(string nodeId, params ExecutableNode[] children) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "Elsa.Testing.Probe",
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Elsa.Testing.Probe")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            childSlots: children.Length == 0 ? null : [new ExecutableChildSlot("Body", children)]);

    /// <summary>
    /// Wraps a node tree in an executable whose identity the hasher derives from its own content — dependency
    /// edges included, which is what makes a cycle unconstructible through this path.
    /// </summary>
    public static WorkflowExecutable Executable(
        string definitionId,
        ExecutableNode rootActivity,
        string artifactVersion = "1.0.0",
        params WorkflowExecutableDependency[] dependencies)
    {
        var inputContract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);
        var hash = Hasher.ComputeHash(
            rootActivity,
            inputContract,
            dependencies,
            checkpointCadence: null,
            workflowVariables: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

        return Build(
            new WorkflowExecutableIdentity(
                ArtifactId: Hasher.CreateArtifactId("artifact-", hash),
                DefinitionId: definitionId,
                DefinitionVersionId: $"{definitionId}:{artifactVersion}",
                ArtifactVersion: artifactVersion,
                ArtifactHash: hash),
            rootActivity,
            inputContract,
            dependencies);
    }

    /// <summary>An edge from a parent onto <paramref name="child"/>, dispatched by <paramref name="dispatchNodeId"/>.</summary>
    public static WorkflowExecutableDependency DependencyOn(WorkflowExecutable child, string dispatchNodeId) =>
        new(child.Identity.ArtifactId, child.Identity.ArtifactHash, [dispatchNodeId]);

    /// <summary>An edge onto an artifact that was never saved — the incomplete-closure condition.</summary>
    public static WorkflowExecutableDependency DanglingDependencyOn(WorkflowExecutable absentChild, string dispatchNodeId) =>
        DependencyOn(absentChild, dispatchNodeId);

    /// <summary>Saves the artifact and mints the Published source reference a publish would have written.</summary>
    public async Task<WorkflowExecutableSourceReference> PublishAsync(
        WorkflowExecutable executable,
        DateTimeOffset? deletedAt = null)
    {
        await Executables.SaveAsync(executable);
        return await AddReferenceAsync(executable, WorkflowExecutableReferenceScope.Published, deletedAt);
    }

    /// <summary>Saves the artifact with no source reference at all — a dependency nobody published on its own.</summary>
    public ValueTask SaveArtifactAsync(WorkflowExecutable executable) => Executables.SaveAsync(executable);

    /// <summary>
    /// Saves an artifact under a caller-chosen identity, so a test can model a store the compiler could not have
    /// written: content that contradicts its own declared hash, or an edge that closes a cycle.
    /// </summary>
    public async Task<WorkflowExecutable> SaveCorruptedAsync(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        params WorkflowExecutableDependency[] dependencies)
    {
        var corrupted = Build(
            identity,
            rootActivity,
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies);
        await Executables.SaveAsync(corrupted);
        return corrupted;
    }

    /// <summary>Adds one more source reference to an already-saved artifact, in any scope.</summary>
    public async Task<WorkflowExecutableSourceReference> AddReferenceAsync(
        WorkflowExecutable executable,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? deletedAt = null,
        string? definitionVersionId = null)
    {
        var sequence = Interlocked.Increment(ref _referenceSequence);
        var reference = new WorkflowExecutableSourceReference(
            SourceReferenceId: $"ref-{sequence:D3}",
            ArtifactId: executable.Identity.ArtifactId,
            SourceKind: scope == WorkflowExecutableReferenceScope.Published
                ? WorkflowExecutableSourceKinds.WorkflowDefinitionVersion
                : WorkflowExecutableSourceKinds.WorkflowDraftSnapshot,
            SourceId: executable.Identity.DefinitionId,
            SourceVersion: executable.Identity.ArtifactVersion,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: definitionVersionId ?? executable.Identity.DefinitionVersionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            CreatedAt: PublishedAt,
            PublishedAt: scope == WorkflowExecutableReferenceScope.Published ? PublishedAt : null,
            Scope: scope,
            ExpiresAt: scope == WorkflowExecutableReferenceScope.TestRun ? PublishedAt.AddHours(1) : null,
            DeletedAt: deletedAt,
            DeletedReason: deletedAt is null ? null : "activation-replaced",
            ActivationId: scope == WorkflowExecutableReferenceScope.Published ? $"activation-{sequence:D3}" : null,
            SlotId: scope == WorkflowExecutableReferenceScope.Published ? "Default" : null);

        await SourceReferences.SaveAsync(reference);
        return reference;
    }

    /// <summary>Indexes a start-trigger binding against the artifact, as the trigger indexer would at activation.</summary>
    public async Task<WorkflowTriggerBinding> AddTriggerBindingAsync(
        WorkflowExecutable executable,
        string executableNodeId,
        string stimulusType = "Test.Probe")
    {
        var stimulusHash = $"sha256:probe-{executableNodeId}";
        var binding = new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(executable.Identity.ArtifactId, executableNodeId, stimulusHash),
            ArtifactId: executable.Identity.ArtifactId,
            DefinitionId: executable.Identity.DefinitionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            ArtifactHash: executable.Identity.ArtifactHash,
            ExecutableNodeId: executableNodeId,
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            CreatedAt: PublishedAt);

        return await TriggerBindings.SaveAsync(binding);
    }

    private static WorkflowExecutable Build(
        WorkflowExecutableIdentity identity,
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies) =>
        new(
            identity: identity,
            rootActivity: rootActivity,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: PublishedAt,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: inputContract,
            dependencies: dependencies,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            checkpointCadence: null,
            workflowVariables: []);
}
