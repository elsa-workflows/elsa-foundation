using System.Reflection;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// SC-B-001 and SC-B-005: a runtime-only engine — execution features plus artifact reconciliation, and nothing
/// from Design or Publishing — imports a mounted artifact and executes it, including a trigger-started workflow,
/// and its assembly closure contains no Design or Publishing assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the claim is proved structurally rather than by a process-wide assembly snapshot.</b> This suite
/// deliberately references <c>Elsa.Workflows.Publishing</c>, <c>Elsa.Workflows.Design.Api</c> and
/// <c>Elsa.Activities.Design.Api</c> so its other guards can inspect them, and xunit runs every class in one
/// process. <c>AppDomain.CurrentDomain.GetAssemblies()</c> therefore reports those assemblies as loaded no matter
/// what this composition does, and asserting on it would test the test host's reference list rather than the
/// engine. The two assertions below cannot be fooled that way: the <b>transitive reference closure</b> of the
/// composed features is a property of the shipped binaries, and the <b>composed container</b> is the actual object
/// graph the engine runs on.
/// </para>
/// <para>
/// The execution test is what stops the structural assertions from being vacuous — a closure that references
/// nothing forbidden is only interesting if the same composition can also import and run a workflow.
/// </para>
/// </remarks>
public sealed class RuntimeOnlyArtifactCompositionTests : IDisposable
{
    /// <summary>
    /// The assembly-name prefixes a runtime-only engine must not depend on. <c>Elsa.Workflows.Publishing</c> has no
    /// trailing dot: the engine assembly itself is the one that must stay out, not only its sub-packages.
    /// </summary>
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Elsa.Workflows.Design",
        "Elsa.Workflows.Publishing",
        "Elsa.Activities.Design"
    ];

    private const string SourceId = "mounted-artifacts";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-runtime-only-composition",
        Guid.NewGuid().ToString("N"));

    public RuntimeOnlyArtifactCompositionTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public void The_runtime_only_composition_references_no_design_or_publishing_assembly()
    {
        var closure = TransitiveElsaClosure(
            typeof(SerializationFeature).Assembly,
            typeof(WorkflowsRuntimeApiFeature).Assembly,
            typeof(WorkflowsRuntimeTriggersFeature).Assembly,
            typeof(ActivitiesRuntimeFeature).Assembly,
            typeof(ActivitiesPrimitivesFeature).Assembly,
            typeof(JsonWorkflowArtifactReconciliationFeature).Assembly);

        // A traversal that silently found nothing would pass every containment check below.
        Assert.True(closure.Count > 15, $"The traversal only reached {closure.Count} Elsa assemblies; it is not walking the graph.");
        Assert.Contains("Elsa.Workflows.Runtime.Reconciliation", closure);
        Assert.Contains("Elsa.Workflows.Runtime.Core", closure);

        var forbidden = closure.Where(IsForbidden).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            forbidden.Length == 0,
            "Enabling artifact reconciliation pulled Design/Publishing into the runtime closure: " + string.Join(", ", forbidden));
    }

    [Fact]
    public void The_composed_container_registers_nothing_from_design_or_publishing()
    {
        var services = RuntimeOnlyServices();

        var offenders = services
            .SelectMany(descriptor => new[]
            {
                descriptor.ServiceType,
                descriptor.ImplementationType,
                descriptor.ImplementationInstance?.GetType()
            })
            .OfType<Type>()
            .SelectMany(Unwrap)
            .Select(type => type.Assembly.GetName().Name)
            .OfType<string>()
            .Where(IsForbidden)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, "Forbidden assemblies appear in the composed container: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task The_runtime_only_composition_imports_and_executes_a_mounted_artifact()
    {
        await using var harness = BuildRuntimeOnlyEngine();

        var plain = NewArtifact(WorkflowExecutionHarness.NewProbeNode("node-plain"), "definition-plain");
        var triggered = NewArtifact(AsStartTrigger(WorkflowExecutionHarness.NewProbeNode("node-trigger")), "definition-triggered");
        Mount(harness, "plain.json", plain);
        Mount(harness, "triggered.json", triggered);

        harness.InitializeActivityTypes();
        WorkflowArtifactReconciliationResult result;
        await using (var reconcileScope = harness.Services.CreateAsyncScope())
            result = await reconcileScope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.RejectedCount);

        // SC-B-001, half one: a mounted, dependency-satisfied artifact runs to completion.
        var referenceStore = harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var plainActivationId = result.Entries.Single(entry => entry.DefinitionId == "definition-plain").ActivationId!;
        var plainReference = await referenceStore.FindAsync(WorkflowActivationReferenceIdentity.Create(plainActivationId));
        Assert.NotNull(plainReference);
        await harness.StartPublishedAsync(plainReference!, WorkflowExecutionHarness.WorkflowExecutionId);
        (await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId)).AssertWorkflowCompleted();

        // SC-B-001, half two: the trigger-started workflow executes when its stimulus arrives.
        await using var routeScope = harness.Services.CreateAsyncScope();
        var routing = await routeScope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(
            new StimulusDispatchRequest(
                TriggerStimulusType,
                TriggerStimulusHash("node-trigger"),
                mode: StimulusRoutingMode.StartOnly,
                idempotencyKey: "composition-stimulus"));

        Assert.Equal(1, routing.StartedCount);
        var started = Assert.Single(routing.Starts);
        Assert.Equal(triggered.Identity.ArtifactId, started.ArtifactId);
        (await harness.ReadRunAsync(started.WorkflowExecutionId!)).AssertWorkflowCompleted();

        // SC-B-005 against the graph that actually ran, not against a static reference list: every service the
        // import and execution path resolved comes from outside Design and Publishing.
        await using var assertionScope = harness.Services.CreateAsyncScope();
        var resolved = new object[]
        {
            assertionScope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>(),
            assertionScope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>(),
            assertionScope.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>(),
            assertionScope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>(),
            assertionScope.ServiceProvider.GetRequiredService<IStimulusRouter>(),
            assertionScope.ServiceProvider.GetRequiredService<IWorkflowTriggerIndexer>(),
            assertionScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(),
            assertionScope.ServiceProvider.GetRequiredService<IActivityActivator>()
        };

        var offenders = resolved
            .Concat(assertionScope.ServiceProvider.GetServices<IActivityActivationStrategy>())
            .Select(service => service.GetType().Assembly.GetName().Name)
            .OfType<string>()
            .Where(IsForbidden)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, "The executing engine resolved services from: " + string.Join(", ", offenders));
    }

    private static bool IsForbidden(string assemblyName) =>
        ForbiddenAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Walks the Elsa half of the reference graph, recording every assembly name it reaches.</summary>
    /// <remarks>
    /// Names that cannot be loaded are recorded rather than skipped: an unresolvable reference is still a
    /// dependency, and dropping it is exactly how a forbidden edge would slip through unnoticed.
    /// </remarks>
    private static IReadOnlyCollection<string> TransitiveElsaClosure(params Assembly[] roots)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(roots);

        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            if (assembly.GetName().Name is not { } name || !visited.Add(name))
                continue;

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is not { } referenceName ||
                    !referenceName.StartsWith("Elsa", StringComparison.Ordinal) ||
                    visited.Contains(referenceName))
                    continue;

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    visited.Add(referenceName);
                }
            }
        }

        return visited;
    }

    /// <summary>Yields a type plus its generic arguments, so <c>ILogger&lt;SomethingForbidden&gt;</c> is caught.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        if (!type.IsGenericType)
            yield break;

        foreach (var argument in type.GetGenericArguments())
            yield return argument;
    }

    private IServiceCollection RuntimeOnlyServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SerializationFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        NewReconciliationFeature().ConfigureServices(services);
        return services;
    }

    private WorkflowExecutionHarness BuildRuntimeOnlyEngine() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => NewReconciliationFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
                services.AddSingleton<IActivityTriggerStimulusProvider, CompositionProbeTriggerStimulusProvider>();
            })
            .Build(Enumerable.Range(1, 32).Select(index => $"activity-execution-{index}").ToArray());

    private JsonWorkflowArtifactReconciliationFeature NewReconciliationFeature() =>
        new() { Options = { SourceId = SourceId, FolderPath = _mount } };

    private void Mount(WorkflowExecutionHarness harness, string fileName, WorkflowExecutable artifact)
    {
        var closure = new WorkflowArtifactClosure(
            WorkflowArtifactClosureFormat.CurrentVersion,
            artifact.Identity.ArtifactId,
            [artifact],
            [],
            []);
        File.WriteAllText(
            Path.Combine(_mount, fileName),
            harness.Services.GetRequiredService<IPayloadSerializer>().Serialize(closure));
    }

    /// <summary>The stimulus type this test's trigger provider advertises.</summary>
    internal const string TriggerStimulusType = "Composition.Probe";

    /// <summary>The stimulus hash this test's trigger provider derives for a node.</summary>
    internal static string TriggerStimulusHash(string nodeId) => $"sha256:composition-{nodeId}";

    /// <summary>
    /// Re-stamps a probe node as a start trigger with the consumer-keyed descriptor a compiled artifact carries.
    /// </summary>
    private static ExecutableNode AsStartTrigger(ExecutableNode node) =>
        new(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: node.DescriptorPayload,
            inputBindings: node.InputBindings,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType,
            },
            childSlots: node.ChildSlots,
            structure: node.Structure,
            activityContract: node.ActivityContract,
            intrinsicKind: node.IntrinsicKind,
            intrinsicVariable: node.IntrinsicVariable,
            outputCaptures: node.OutputCaptures,
            descriptorSchemaVersion: node.DescriptorSchemaVersion);

    /// <summary>Builds a content-addressed artifact whose declared hash and id the importer's gate will recompute.</summary>
    private static WorkflowExecutable NewArtifact(ExecutableNode rootActivity, string definitionId)
    {
        var pinned = StringComparer.Ordinal.Equals(rootActivity.DescriptorType, WellKnownRuntimeActivityConsumers.ClrActivity)
            ? rootActivity
            : AsConsumerKeyed(rootActivity);
        var hasher = new WorkflowExecutableHasher();
        var inputContract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);
        var dependencies = Array.Empty<WorkflowExecutableDependency>();
        var variables = Array.Empty<RuntimeVariableDeclaration>();
        var hash = hasher.ComputeHash(
            pinned,
            inputContract,
            dependencies,
            checkpointCadence: null,
            workflowVariables: variables,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                hasher.CreateArtifactId("artifact-", hash),
                definitionId,
                $"{definitionId}:1.0.0",
                "1.0.0",
                hash),
            rootActivity: pinned,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: inputContract,
            dependencies: dependencies,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            checkpointCadence: null,
            workflowVariables: variables);
    }

    private static ExecutableNode AsConsumerKeyed(ExecutableNode node) =>
        new(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: node.DescriptorPayload,
            inputBindings: node.InputBindings,
            metadata: node.Metadata,
            childSlots: node.ChildSlots,
            structure: node.Structure,
            activityContract: node.ActivityContract,
            intrinsicKind: node.IntrinsicKind,
            intrinsicVariable: node.IntrinsicVariable,
            outputCaptures: node.OutputCaptures,
            descriptorSchemaVersion: node.DescriptorSchemaVersion);
}

/// <summary>Recognizes this test's probe trigger nodes so the runtime can index and route them.</summary>
internal sealed class CompositionProbeTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => "Composition.ProbeTrigger";

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) ||
            !StringComparer.Ordinal.Equals(executionType, TriggerNodeMetadata.TriggerExecutionType))
            return ActivityTriggerStimulusResult.NotRecognized;

        return ActivityTriggerStimulusResult.Recognized(
        [
            new TriggerStimulusDescriptor(
                RuntimeOnlyArtifactCompositionTests.TriggerStimulusType,
                RuntimeOnlyArtifactCompositionTests.TriggerStimulusHash(node.ExecutableNodeId))
        ]);
    }
}
