using System.Reflection;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Events;
using Elsa.Mediator.Core.Contracts;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Architecture.Tests;

/// <summary>
/// SC-B-006, the two legs the suite was missing: a <b>design-only</b> composition and a <b>combined</b> one are
/// each valid and pass their smoke test. The third leg — runtime-only — is
/// <see cref="RuntimeOnlyArtifactCompositionTests"/> and is deliberately not restated here.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a composition-validity matrix has to say, and what it must not pretend to say.</b> Each leg asserts
/// that its shape <em>activates</em> — the container builds and the services that shape is supposed to own really
/// resolve out of it — and that the services belonging to the <em>other</em> half are absent or, in the combined
/// leg, present and still served by their own side. It is not a behavioural test of either half; the deep
/// behaviour lives in
/// <see cref="CombinedEnginePublishExecuteRegressionTests"/>, <see cref="DualReconciliationOwnershipTests"/> and
/// <see cref="ArtifactExportImportRoundTripTests"/>. What the matrix adds is the claim §E2.2.3 actually makes:
/// all three deployment shapes remain composable once artifact reconciliation exists.
/// </para>
/// <para>
/// <b>Absence is asserted twice, by independent mechanisms.</b> The composed container is the object graph the
/// host would run on, and the transitive reference closure is a property of the shipped binaries. Neither alone is
/// enough: a container check cannot see a binary edge nobody registered yet, and a closure check cannot see a
/// service someone registered by hand. This assembly references Publishing, Design and Reconciliation on purpose
/// so its other guards can inspect them, so an <c>AppDomain</c> assembly snapshot would prove nothing here.
/// </para>
/// <para>
/// <b>The combined leg's sharpest assertion is the capability set.</b>
/// <c>TryAddEnumerable(Singleton&lt;IRuntimeActivityConsumerCapability, T&gt;())</c> de-duplicates by
/// <em>implementation type</em>, so two features that both advertised through one shared
/// <c>RuntimeActivityConsumerCapability</c> record silently collapsed into a single registration earlier in this
/// feature — and the importer's requirement gate then refused artifacts the engine could run. The combined shape
/// is where that class of loss would reappear, because it is the shape with the most features contending, so the
/// resolved implementation types are asserted rather than merely counted.
/// </para>
/// </remarks>
public sealed class ArtifactCompositionMatrixTests : IDisposable
{
    /// <summary>The assembly the runtime-only artifact importer ships in — the one a design-only host must not reach.</summary>
    private const string ArtifactReconciliationAssembly = "Elsa.Workflows.Runtime.Reconciliation";

    private const string PublishedDefinitionId = "definition-combined-published";
    private const string PublishedVersionId = "version-combined-published";
    private const string PublishedNodeId = "node-combined-published";
    private const string PublishedEventName = "combined-published-event";

    private const string ImportedDefinitionId = "definition-combined-imported";
    private const string ImportedVersionId = "version-combined-imported";
    private const string ImportedNodeId = "node-combined-imported";
    private const string ImportedEventName = "combined-imported-event";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-composition-matrix",
        Guid.NewGuid().ToString("N"));

    public ArtifactCompositionMatrixTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    // ---- Design-only ----------------------------------------------------------------------------------------

    /// <summary>
    /// The design-only shape activates: an authoring host composes structure projection and draft validation, and
    /// holds nothing of the runtime or of artifact reconciliation.
    /// </summary>
    /// <remarks>
    /// The services are <em>resolved</em>, not looked for on descriptors. A descriptor proves a feature ran its
    /// <c>ConfigureServices</c>; only a resolution proves the graph behind it closes — which is the whole content
    /// of "this composition is valid". Scope validation is on for the same reason.
    /// </remarks>
    [Fact]
    public void The_design_only_composition_activates_and_holds_no_runtime_artifact_services()
    {
        using var provider = BuildDesignOnlyHost();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        // Authoring: the structure seam and its handler set, plus the baseline draft validators that hang off it.
        Assert.NotNull(services.GetRequiredService<IActivityStructureService>());
        Assert.Contains(
            services.GetServices<IActivityStructureHandler>(),
            handler => StringComparer.Ordinal.Equals(handler.Kind, BpmnProcessActivity.StructureKind));
        Assert.NotEmpty(services.GetServices<IDraftValidator>());

        // Authoring persistence: the authored documents a design host reads and writes.
        Assert.NotNull(services.GetRequiredService<IWorkflowDefinitionVersionStore>());
        Assert.NotNull(services.GetRequiredService<IActivityDefinitionVersionStore>());

        // The runtime half — and artifact reconciliation with it — is not merely unused here, it is unresolvable.
        Assert.Null(services.GetService<IWorkflowArtifactReconciler>());
        Assert.Null(services.GetService<IWorkflowExecutableStore>());
        Assert.Null(services.GetService<IWorkflowExecutableSourceReferenceStore>());
        Assert.Null(services.GetService<IWorkflowActivationAuthority>());
        Assert.Null(services.GetService<IWorkflowActivationCoordinator>());
        Assert.Null(services.GetService<IStimulusRouter>());
        Assert.Null(services.GetService<IActivityActivator>());
        Assert.Empty(services.GetServices<IRuntimeActivityConsumerCapability>());
    }

    /// <summary>
    /// The other half of the design-only claim, made about the shipped binaries: nothing the design-only host
    /// composes can reach the artifact-reconciliation assembly through the reference graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The forbidden name is scoped to <c>Elsa.Workflows.Runtime.Reconciliation</c> rather than to
    /// <c>Elsa.Workflows.Runtime</c> at large, and that is deliberate rather than lenient: an activity's
    /// <c>.Design</c> package legitimately references its own <c>.Runtime</c> half (T128 split them precisely so
    /// the runtime half could stand alone, not so the design half could), so a blanket runtime ban would be a
    /// claim §E2.2 does not make. What §E2.2.3 does require of this shape is that the importer — the thing spec
    /// 151 adds — stays out of it.
    /// </para>
    /// <para>
    /// The walker is local rather than shared with <see cref="RuntimeEngines.RuntimeOnlyAssemblyClosure"/>: that
    /// one is pinned to the runtime-only roots and carries the runtime-only leg's own sanity assertions, and
    /// widening it would mean rewriting a guard this task must leave alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_design_only_composition_references_no_artifact_reconciliation_assembly()
    {
        var closure = TransitiveElsaClosure(
            typeof(SerializationFeature).Assembly,
            typeof(EventsFeature).Assembly,
            typeof(WorkflowDesignValidationsFeature).Assembly,
            typeof(ActivitiesBpmnDesignFeature).Assembly,
            typeof(IWorkflowDefinitionVersionStore).Assembly,
            typeof(IActivityDefinitionLookup).Assembly);

        // A traversal that silently found nothing would satisfy the containment check below.
        Assert.True(closure.Count > 10, $"The traversal only reached {closure.Count} Elsa assemblies; it is not walking the graph.");
        Assert.Contains("Elsa.Workflows.Design.Core", closure);
        Assert.DoesNotContain(ArtifactReconciliationAssembly, closure);
    }

    // ---- Combined -------------------------------------------------------------------------------------------

    /// <summary>
    /// The combined shape activates: one container holds the design/publish half and the runtime half — artifact
    /// reconciliation included — and each contract is still served by its own side.
    /// </summary>
    /// <remarks>
    /// The services are resolved and then their <em>implementation assemblies</em> are inspected, rather than being
    /// counted as present. "Both halves are here" is satisfied by a container in which one side quietly answers for
    /// the other — a design-side stand-in registered against a runtime contract, say — and that container would be
    /// a combined shape in name only. Asserting which family each resolved instance came from is the difference.
    /// </remarks>
    [Fact]
    public async Task The_combined_composition_activates_both_halves_in_one_container()
    {
        await using var engine = CombinedEngine.Create([PublishedVersion()], _mount);
        await using var scope = engine.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var designHalf = new object[]
        {
            services.GetRequiredService<IActivityStructureService>(),
            services.GetRequiredService<IWorkflowExecutableCompiler>(),
            services.GetRequiredService<IWorkflowArtifactClosureFactory>(),
            services.GetRequiredService<IRequestHandler<PublishWorkflow, PublishedWorkflowView>>()
        };

        var runtimeHalf = new object[]
        {
            services.GetRequiredService<IWorkflowArtifactReconciler>(),
            services.GetRequiredService<IWorkflowArtifactClosureSerializer>(),
            services.GetRequiredService<IWorkflowExecutableStore>(),
            services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            services.GetRequiredService<IWorkflowActivationAuthority>(),
            services.GetRequiredService<IWorkflowActivationCoordinator>(),
            services.GetRequiredService<IStimulusRouter>(),
            services.GetRequiredService<IActivityActivator>()
        };

        // The authored documents the design half reads are here too, and they are the design stores.
        Assert.NotNull(services.GetRequiredService<IWorkflowDefinitionVersionStore>());
        Assert.NotNull(services.GetRequiredService<IActivityDefinitionVersionStore>());

        Assert.All(designHalf, service => Assert.True(
            IsDesignOrPublishing(AssemblyNameOf(service)),
            $"A design-half contract was served from outside Design/Publishing: {service.GetType().FullName}"));
        Assert.All(runtimeHalf, service => Assert.False(
            IsDesignOrPublishing(AssemblyNameOf(service)),
            $"A runtime-half contract was served from Design/Publishing: {service.GetType().FullName}"));

        // And the importer specifically is the shipped one, not something the design half supplied.
        Assert.Equal(
            ArtifactReconciliationAssembly,
            AssemblyNameOf(services.GetRequiredService<IWorkflowArtifactReconciler>()));
    }

    /// <summary>
    /// The combined shape advertises one consumer capability per implementation type and per consumer key — no
    /// registration was silently swallowed by <c>TryAddEnumerable</c>'s implementation-type de-duplication.
    /// </summary>
    /// <remarks>
    /// Both halves of the check are load-bearing and neither implies the other. Distinct <em>implementation
    /// types</em> is what <c>TryAddEnumerable</c> keys on, so a collision there is a lost registration; distinct
    /// <em>consumer keys</em> is what <c>RuntimeRequirementChecker</c> matches an artifact's declared requirements
    /// against, so a collision there is an ambiguous gate. The named types are pinned so the set cannot pass by
    /// shrinking to one.
    /// </remarks>
    [Fact]
    public async Task The_combined_composition_advertises_one_capability_per_consumer_key()
    {
        await using var engine = CombinedEngine.Create([PublishedVersion()], _mount);

        var capabilities = engine.Services.GetServices<IRuntimeActivityConsumerCapability>().ToArray();
        var implementationTypes = capabilities.Select(capability => capability.GetType()).ToArray();

        var duplicateTypes = implementationTypes
            .GroupBy(type => type)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            duplicateTypes.Length == 0,
            "One implementation type advertises more than one capability: " + string.Join(", ", duplicateTypes));

        var duplicateKeys = capabilities
            .GroupBy(capability => capability.ConsumerKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            duplicateKeys.Length == 0,
            "More than one capability claims the same consumer key: " + string.Join(", ", duplicateKeys));

        // The two the combined shape must have: the CLR activation seam every authored node compiles onto, and the
        // engine intrinsic the compiler stamps with the reserved "intrinsic" consumer key. Pinned so the checks
        // above cannot be satisfied by a set that collapsed to one entry — or to none.
        Assert.Contains(typeof(ClrActivityConsumerCapability), implementationTypes);
        Assert.Contains(typeof(WorkflowIntrinsicActivityConsumerCapability), implementationTypes);
    }

    /// <summary>
    /// The combined shape's smoke test: on one engine, a definition published through the production publish
    /// handler and a <em>different</em> definition imported from the mount are both activated under their own
    /// source, and each one's stimulus starts exactly one instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two different definitions on purpose. The contention case — one definition arriving through both paths — is
    /// <see cref="DualReconciliationOwnershipTests"/>; what is being smoke-tested here is that the two paths both
    /// <em>work</em> on one container, which a contention test cannot show because in it one path is always the
    /// one that loses.
    /// </para>
    /// <para>
    /// The imported artifact is exported from a separate engine and travels as wire bytes through the mount, so
    /// the importing half is exercised end to end rather than handed an in-memory closure.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_combined_composition_serves_a_published_and_an_imported_definition_side_by_side()
    {
        await MountImportedClosureAsync();

        await using var engine = CombinedEngine.Create([PublishedVersion()], _mount);
        await engine.PublishAsync(PublishedVersionId);

        var result = await engine.ReconcileAsync();
        var entry = Assert.Single(result.Entries);
        Assert.Equal(ImportedDefinitionId, entry.DefinitionId);

        // Each definition is activated, and by the source that actually put it there.
        var publishedSlot = await engine.FindSlotAsync(PublishedDefinitionId);
        var importedSlot = await engine.FindSlotAsync(ImportedDefinitionId);
        Assert.NotNull(publishedSlot);
        Assert.NotNull(importedSlot);
        Assert.Equal(WorkflowActivationSource.PublishingKind, publishedSlot!.Source!.Kind);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, importedSlot!.Source!.Kind);
        Assert.NotEqual(publishedSlot.ActiveActivationId, importedSlot.ActiveActivationId);

        // Serving state, not a projection of the slot: what the stimulus router would actually route on.
        var publishedBinding = Assert.Single(await engine.ListServingBindingsAsync(PublishedEventName));
        var importedBinding = Assert.Single(await engine.ListServingBindingsAsync(ImportedEventName));
        Assert.Equal(publishedSlot.ActiveActivationId, publishedBinding.ActivationId);
        Assert.Equal(importedSlot.ActiveActivationId, importedBinding.ActivationId);

        // And both actually run, from the same container, one instance each.
        var publishedRouting = await engine.DeliverEventAsync(PublishedEventName, "matrix-published");
        var importedRouting = await engine.DeliverEventAsync(ImportedEventName, "matrix-imported");
        Assert.Equal(1, publishedRouting.StartedCount);
        Assert.Equal(1, importedRouting.StartedCount);

        // Two runs, one per definition, each pinned to the artifact its own path put on the slot — so neither
        // stimulus was served by the other half's artifact.
        var executions = await engine.ListExecutionsAsync();
        Assert.Equal(2, executions.Count);
        Assert.All(executions, execution => Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status));
        Assert.Equal(
            new[] { publishedBinding.ArtifactId, importedBinding.ArtifactId }.Order(StringComparer.Ordinal).ToArray(),
            executions.Select(execution => execution.PinnedExecutable.ArtifactId).Order(StringComparer.Ordinal).ToArray());
    }

    // ---- Compositions ---------------------------------------------------------------------------------------

    /// <summary>
    /// A design-only host: authoring structure, draft validation and the design catalogs — and no execution spine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Composed here rather than through <see cref="RuntimeEngines"/> on purpose. Every builder there starts from
    /// <c>WorkflowExecutionHarness</c>, which <em>is</em> the runtime execution spine; a design-only shape is
    /// defined by not having one, so building it from that helper would defeat the assertion it exists to support.
    /// </para>
    /// <para>
    /// The structure handler comes from <see cref="ActivitiesBpmnDesignFeature"/> because it is the one shipped
    /// <c>Activities.*.Design</c> feature already on this project's reference graph (through
    /// <c>Elsa.Activities.Bpmn.Interchange</c>). Any of the composite design packages T128 split out would do; the
    /// claim is about the design host resolving a real, shipped handler, not about BPMN.
    /// </para>
    /// </remarks>
    private static ServiceProvider BuildDesignOnlyHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();

        new SerializationFeature().ConfigureServices(services);
        new EventsFeature().ConfigureServices(services);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);

        // No in-memory design store ships in src, and the subject here is composition rather than catalog content,
        // so the design catalogs are supplied empty. An empty catalog still closes every validator's graph.
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new PublishCapableEngine.StubWorkflowVersionStore());
        services.AddSingleton<IActivityDefinitionVersionStore>(new PublishCapableEngine.StubActivityVersionStore());
        services.AddScoped<IActivityDefinitionLookup, EmptyActivityDefinitionCatalog>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static WorkflowDefinitionVersion PublishedVersion() =>
        CombinedEngine.EventWorkflow(PublishedDefinitionId, PublishedVersionId, "1.0.0", PublishedNodeId, PublishedEventName);

    /// <summary>Publishes the imported definition on a separate engine and writes its closure into the mount.</summary>
    private async Task MountImportedClosureAsync()
    {
        await using var exporter = CombinedEngine.Create(
        [
            CombinedEngine.EventWorkflow(ImportedDefinitionId, ImportedVersionId, "1.0.0", ImportedNodeId, ImportedEventName)
        ]);
        await exporter.PublishAsync(ImportedVersionId);
        await exporter.ExportToAsync(ImportedVersionId, _mount, "imported.json");
    }

    private static string AssemblyNameOf(object service) => service.GetType().Assembly.GetName().Name!;

    /// <summary>
    /// Whether an assembly belongs to the design/publish family — the half a runtime-only shape excludes.
    /// </summary>
    /// <remarks>
    /// The same three prefixes <see cref="RuntimeOnlyArtifactCompositionTests"/> forbids, read here as a
    /// <em>classifier</em> rather than as a ban: in the combined shape both families are expected, and what is
    /// being asserted is that each contract is served by the right one.
    /// </remarks>
    private static bool IsDesignOrPublishing(string assemblyName) =>
        assemblyName.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal) ||
        assemblyName.StartsWith("Elsa.Workflows.Publishing", StringComparison.Ordinal) ||
        assemblyName.StartsWith("Elsa.Activities.Design", StringComparison.Ordinal);

    /// <summary>Walks the Elsa half of the reference graph, recording every assembly name it reaches.</summary>
    /// <remarks>
    /// Names that cannot be loaded are recorded rather than skipped: an unresolvable reference is still a
    /// dependency, and dropping it is exactly how a forbidden edge would slip through unnoticed.
    /// </remarks>
    private static IReadOnlyCollection<string> TransitiveElsaClosure(params Assembly[] roots)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(roots);

        while (pending.TryDequeue(out var assembly))
        {
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

    /// <summary>An empty authoring catalog — enough for the design-only graph to close, and nothing more.</summary>
    private sealed class EmptyActivityDefinitionCatalog : IActivityDefinitionLookup
    {
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            throw new ArgumentException($"Unknown activity definition '{idOrActivityTypeKey}'.");

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
            string? id = null,
            string? category = null,
            string? searchTerm = null,
            string? displayName = null,
            string? description = null,
            bool? tenantAgnostic = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<IActivityDefinition>());

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            throw new ArgumentException($"Unknown activity version '{versionId}'.");

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(null);

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<ActivityDefinitionVersionSummary>());
    }
}
