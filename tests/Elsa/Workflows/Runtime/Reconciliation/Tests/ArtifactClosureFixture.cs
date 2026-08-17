using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Primitives.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Builds portable closure envelopes the way a real exporter would: a content-addressed executable whose declared
/// hash and id are produced by the production <see cref="WorkflowExecutableHasher"/>, serialized through the
/// engine's own payload serializer and written to a folder the importer mounts.
/// </summary>
/// <remarks>
/// Nothing here fakes an identity. The importer's step-2a gate recomputes each member's canonical hash before any
/// write, so an envelope built with a hand-written hash would be rejected — which means these fixtures exercise
/// the real content-addressing round-trip (build → serialize → file → deserialize → recompute) rather than
/// bypassing it.
/// </remarks>
internal static class ArtifactClosureFixture
{
    /// <summary>The fixed creation timestamp every fixture artifact carries.</summary>
    public static readonly DateTimeOffset CreatedAt = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The stimulus type the fixture's trigger nodes advertise through <see cref="ProbeTriggerStimulusProvider"/>.</summary>
    public const string TriggerStimulusType = "Test.Probe";

    private static readonly IWorkflowExecutableHasher Hasher = new WorkflowExecutableHasher();

    /// <summary>
    /// A leaf probe node in the shape a compiler-produced artifact actually carries: a pinned CLR activity
    /// contract and a <b>consumer-keyed</b> descriptor.
    /// </summary>
    /// <remarks>
    /// The consumer key matters and is easy to get wrong. <see cref="WorkflowExecutionHarness.NewProbeNode"/>
    /// stamps the descriptor's CLR type name and relies on the harness rewriting it to the consumer key when it
    /// pins contracts on save. The importer deliberately never rewrites an artifact — content-addressed bytes are
    /// what they are — so a fixture that skipped this step would mount a workflow the activator cannot resolve.
    /// </remarks>
    public static ExecutableNode ProbeNode(string nodeId) => Rebuild(WorkflowExecutionHarness.NewProbeNode(nodeId), metadata: null);

    /// <summary>
    /// The same node re-stamped as a start trigger. The metadata key is what the runtime's binding extractor scans
    /// for; without it a mounted workflow is importable but can never be started by a stimulus.
    /// </summary>
    public static ExecutableNode AsStartTrigger(ExecutableNode node) =>
        Rebuild(node, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType,
        });

    /// <summary>
    /// A start-trigger node for a <b>real</b> CLR trigger activity (Timer, Cron, HttpEndpoint, …), carrying one
    /// authored literal input and pinned into the form a compiler would emit.
    /// </summary>
    /// <remarks>
    /// The pinning is not cosmetic. A start trigger's stimulus and schedule are fixed at publish time, so the
    /// providers read the literal off the node rather than off a running instance; and the descriptor must carry
    /// the <em>consumer key</em>, because a content-addressed artifact is never rewritten on import. Delegating
    /// both to <see cref="WorkflowExecutionHarness.NewPinnedClrNode"/> keeps the fixture from re-deriving a shape
    /// that is only correct in one exact way.
    /// </remarks>
    public static ExecutableNode ClrTriggerNode(string nodeId, string activityType, string inputKey, string literal)
    {
        var valueType = new ValueTypeDescriptor("String");
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(literal));
        var binding = new RuntimeInputBinding(
            inputKey,
            valueType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(valueType, value.RootElement.Clone(), ValueProtectionPolicy.InstanceInline));

        using var descriptorPayload = JsonDocument.Parse("""{"kind":"placeholder"}""");
        var unpinned = new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                "placeholder",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                descriptorPayload.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase) { [inputKey] = binding },
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType,
            });

        return WorkflowExecutionHarness.NewPinnedClrNode(unpinned);
    }

    /// <summary>
    /// An <b>engine-intrinsic</b> node in exactly the shape <c>ExecutableNodeCompiler</c> emits: the reserved
    /// <c>"intrinsic"</c> descriptor type, an <c>elsa.intrinsic.*</c> activity type, and the compiler's own
    /// descriptor payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other fixture node here is CLR-activated, which is precisely why the intrinsic consumer gap survived
    /// undetected: <c>WorkflowExecutable</c>'s constructor derives <c>RuntimeRequirements</c> from every node's
    /// consumer key, so an artifact carrying one of these declares <c>RuntimeRequirement("intrinsic", "1")</c> and
    /// exercises the requirement axis against a consumer no activity package can advertise.
    /// </para>
    /// <para>
    /// The descriptor's schema version is left unset on purpose: the compiler does not pass one either, so
    /// <c>ExecutableNode</c> defaults it to <c>RuntimeActivityDescriptor.InitialSchemaVersion</c>. Stamping one
    /// here would make the fixture disagree with the compiler about the very value the gate matches on.
    /// </para>
    /// </remarks>
    public static ExecutableNode IntrinsicNode(
        string nodeId,
        WorkflowIntrinsicKind kind = WorkflowIntrinsicKind.SetCorrelationId,
        string inputKey = WorkflowIntrinsicInputKeys.Value,
        string literal = "correlation-42")
    {
        var valueType = new ValueTypeDescriptor("String");
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: $"elsa.intrinsic.{kind.ToString().ToLowerInvariant()}",
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.Intrinsic,
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = kind.ToString(), schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
            {
                [inputKey] = new(
                    inputKey,
                    valueType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(
                        valueType,
                        JsonSerializer.SerializeToElement(literal),
                        ValueProtectionPolicy.InstanceInline)),
            },
            metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            intrinsicKind: kind);
    }

    /// <summary>
    /// A CLR node in the correct <em>shape</em> — consumer-keyed descriptor, readable payload — whose declared
    /// <c>TypeAlias</c> names an activity this runtime does not have. The US2 scenario-1 artifact.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than through <see cref="WorkflowExecutionHarness.NewPinnedClrNode"/>, which reflects
    /// over loaded assemblies to pin a contract and therefore cannot produce a node for a type that is absent —
    /// which is precisely the condition under test.
    /// </remarks>
    public static ExecutableNode UnregisteredClrNode(string nodeId, string typeAlias) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeAlias,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeAlias)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// The same wrapping as <see cref="Executable(ExecutableNode, string, string, WorkflowExecutableDependency[])"/>
    /// with explicitly declared requirement sets, for artifacts that demand capabilities this runtime lacks.
    /// </summary>
    /// <remarks>
    /// Requirements are <b>not</b> hash inputs — <c>IWorkflowExecutableHasher.ComputeHash</c> covers the
    /// root activity, input contract, dependencies, cadence, variables and incident strategy — so an artifact
    /// built here still verifies against the importer's content-hash recompute. That is deliberate: the gate under
    /// test must be the requirements gate, not the integrity gate standing in front of it.
    /// </remarks>
    public static WorkflowExecutable ExecutableRequiring(
        ExecutableNode rootActivity,
        string definitionId,
        IReadOnlyCollection<RuntimeRequirement>? runtimeRequirements = null,
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageDriverRequirements = null,
        string artifactVersion = "1.0.0") =>
        Build(rootActivity, definitionId, artifactVersion, [], runtimeRequirements, storageDriverRequirements);

    /// <summary>
    /// A copy of <paramref name="executable"/> that keeps its identity but carries a <em>different</em> payload, so
    /// the declared hash and id no longer describe the content. What a truncated or mis-merged export looks like.
    /// </summary>
    public static WorkflowExecutable TamperedCopy(WorkflowExecutable executable, ExecutableNode replacementRoot) =>
        new(
            identity: executable.Identity,
            rootActivity: replacementRoot,
            resumeTargets: executable.ResumeTargets,
            createdAt: executable.CreatedAt,
            compatibilityMetadata: executable.CompatibilityMetadata,
            inputContract: executable.InputContract,
            dependencies: [],
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: executable.IncidentStrategy,
            checkpointCadence: executable.CheckpointCadence,
            workflowVariables: executable.WorkflowVariables);

    private static ExecutableNode Rebuild(ExecutableNode node, IReadOnlyDictionary<string, string>? metadata) =>
        new(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptorType: StringComparer.Ordinal.Equals(node.ActivityContract?.DescriptorKind, typeof(ClrActivityDescriptor).FullName)
                ? WellKnownRuntimeActivityConsumers.ClrActivity
                : node.DescriptorType,
            descriptorPayload: node.DescriptorPayload,
            inputBindings: node.InputBindings,
            metadata: metadata ?? node.Metadata,
            childSlots: node.ChildSlots,
            structure: node.Structure,
            activityContract: node.ActivityContract,
            intrinsicKind: node.IntrinsicKind,
            intrinsicVariable: node.IntrinsicVariable,
            outputCaptures: node.OutputCaptures,
            descriptorSchemaVersion: node.DescriptorSchemaVersion);

    /// <summary>
    /// Wraps a node in an executable whose identity is derived from its own content, exactly as the compiler does.
    /// </summary>
    public static WorkflowExecutable Executable(
        ExecutableNode rootActivity,
        string definitionId,
        string artifactVersion = "1.0.0",
        params WorkflowExecutableDependency[] dependencies) =>
        Build(rootActivity, definitionId, artifactVersion, dependencies, null, null);

    private static WorkflowExecutable Build(
        ExecutableNode rootActivity,
        string definitionId,
        string artifactVersion,
        WorkflowExecutableDependency[] dependencies,
        IReadOnlyCollection<RuntimeRequirement>? runtimeRequirements,
        IReadOnlyCollection<RuntimeStorageDriverRequirement>? storageDriverRequirements)
    {
        var inputContract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);
        var workflowVariables = Array.Empty<RuntimeVariableDeclaration>();
        var hash = Hasher.ComputeHash(
            rootActivity,
            inputContract,
            dependencies,
            checkpointCadence: null,
            workflowVariables: workflowVariables,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

        var identity = new WorkflowExecutableIdentity(
            ArtifactId: Hasher.CreateArtifactId("artifact-", hash),
            DefinitionId: definitionId,
            DefinitionVersionId: $"{definitionId}:{artifactVersion}",
            ArtifactVersion: artifactVersion,
            ArtifactHash: hash);

        return new WorkflowExecutable(
            identity: identity,
            rootActivity: rootActivity,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: CreatedAt,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: inputContract,
            dependencies: dependencies,
            runtimeRequirements: runtimeRequirements,
            storageDriverRequirements: storageDriverRequirements,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            checkpointCadence: null,
            workflowVariables: workflowVariables);
    }

    /// <summary>A dependency edge onto <paramref name="child"/>, dispatched from <paramref name="dispatchNodeId"/>.</summary>
    public static WorkflowExecutableDependency DependencyOn(WorkflowExecutable child, string dispatchNodeId) =>
        new(child.Identity.ArtifactId, child.Identity.ArtifactHash, [dispatchNodeId]);

    /// <summary>An envelope rooted at the first artifact, carrying no provenance collections.</summary>
    public static WorkflowArtifactClosure Closure(params WorkflowExecutable[] artifacts) =>
        new(
            WorkflowArtifactClosureFormat.CurrentVersion,
            artifacts[0].Identity.ArtifactId,
            artifacts,
            [],
            []);

    /// <summary>An envelope claiming a wire-format version this build does not know how to read.</summary>
    public static WorkflowArtifactClosure ClosureWithFormatVersion(int formatVersion, params WorkflowExecutable[] artifacts) =>
        new(formatVersion, artifacts[0].Identity.ArtifactId, artifacts, [], []);

    /// <summary>An envelope that also carries the exporting engine's trigger surface as an expectation.</summary>
    public static WorkflowArtifactClosure ClosureWithCarriedBindings(
        WorkflowExecutable artifact,
        string nodeId,
        string stimulusHash) =>
        new(
            WorkflowArtifactClosureFormat.CurrentVersion,
            artifact.Identity.ArtifactId,
            [artifact],
            [],
            [
                new WorkflowTriggerBinding(
                    TriggerBindingId: WorkflowTriggerBinding.BuildId("exporter-activation", artifact.Identity.ArtifactId, nodeId, stimulusHash),
                    ArtifactId: artifact.Identity.ArtifactId,
                    DefinitionId: artifact.Identity.DefinitionId,
                    ArtifactVersion: artifact.Identity.ArtifactVersion,
                    ArtifactHash: artifact.Identity.ArtifactHash,
                    ExecutableNodeId: nodeId,
                    StimulusType: TriggerStimulusType,
                    StimulusHash: stimulusHash,
                    CorrelationScope: null,
                    Metadata: new Dictionary<string, string>(),
                    CreatedAt: CreatedAt,
                    ActivationId: "exporter-activation",
                    SlotId: "exporter-slot")
            ]);

    /// <summary>
    /// Serializes the envelope through the engine's own closure codec — the same encoder the export side uses —
    /// and drops it in the mount folder.
    /// </summary>
    /// <remarks>
    /// Going through <see cref="IWorkflowArtifactClosureSerializer"/> rather than the raw payload serializer is
    /// what makes these fixtures exercise the real export→import bytes: the codec drops the recomputed
    /// <c>Nodes</c>/<c>NodesById</c> projections, so a fixture that bypassed it would mount a document no exporter
    /// would ever produce.
    /// </remarks>
    public static string Mount(IServiceProvider services, string folder, string fileName, WorkflowArtifactClosure closure)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        var serializer = (IWorkflowArtifactClosureSerializer)services.GetService(typeof(IWorkflowArtifactClosureSerializer))!;
        File.WriteAllText(path, serializer.Serialize(closure));
        return path;
    }

    /// <summary>Drops arbitrary bytes in the mount folder — for envelopes no encoder would ever produce.</summary>
    public static string MountRaw(string folder, string fileName, string content)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The stimulus hash the fixture's trigger provider derives for a probe trigger node.</summary>
    public static string TriggerStimulusHash(string nodeId) => $"sha256:probe-{nodeId}";
}

/// <summary>
/// Recognizes the fixture's probe nodes as start triggers.
/// </summary>
/// <remarks>
/// A test-owned provider rather than a real one (HTTP, timer) on purpose: US1 scenario 2 is about the runtime
/// routing a stimulus to an imported artifact, and borrowing a transport activity would drag its host, middleware
/// and clock into a test whose subject is the import→activation→routing chain. The provider is a first-class
/// extension point, so supplying one is composition, not a stub.
/// </remarks>
internal sealed class ProbeTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => "Test.ProbeTrigger";

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) ||
            !StringComparer.Ordinal.Equals(executionType, TriggerNodeMetadata.TriggerExecutionType))
            return ActivityTriggerStimulusResult.NotRecognized;

        return ActivityTriggerStimulusResult.Recognized(
        [
            new TriggerStimulusDescriptor(
                ArtifactClosureFixture.TriggerStimulusType,
                ArtifactClosureFixture.TriggerStimulusHash(node.ExecutableNodeId))
        ]);
    }
}
