using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime;
using Elsa.Events;
using Elsa.Locking.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Json;
using Elsa.Workflows.Design.Reconciliation.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// FR-B-009 (T103): design-side workflow reconciliation and runtime-side executable artifact reconciliation are
/// independently composable — an engine may enable either, both, or neither, and all four compose and start.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Start" means the host's startup path actually runs.</b> Each cell builds a real container with scope
/// validation on and then calls <c>ITaskManager.StartExecutingRegisteredTasks</c> — the same entry point CShells'
/// <c>RunShellTasksInitializer</c> uses — so every registered <see cref="IStartupTask"/> is resolved, topologically
/// sorted and executed, and the background and recurring tasks are started too. Asserting that a
/// <c>ServiceCollection</c> contains descriptors would prove neither that the graph resolves nor that the two
/// reconcilers' startup tasks can coexist in one ordering.
/// </para>
/// <para>
/// <b>Each armed side is asserted by its effect, not by its registration.</b> The artifact side must have imported
/// the mounted closure and left the definition activated under its own ownership; the design side must have
/// materialized the mounted catalog's definition and version. Each unarmed side must have left no trace — which is
/// what makes "independently" more than a word: the cells differ only in which feature is composed, and nothing
/// leaks between them.
/// </para>
/// <para>
/// The two sides are deliberately given <em>different</em> definitions. Composability is the claim here; the
/// contention case where one definition arrives through both paths is <see cref="DualReconciliationOwnershipTests"/>.
/// </para>
/// </remarks>
public sealed class ReconciliationComposabilityTests : IDisposable
{
    private const string ArtifactDefinitionId = "definition-artifact-side";
    private const string ArtifactVersionId = "version-artifact-side";
    private const string ArtifactEventName = "artifact-side-event";

    private const string DesignDefinitionId = "definition-design-side";

    private readonly string _artifactMount = Path.Combine(
        Path.GetTempPath(),
        "elsa-composability-artifacts",
        Guid.NewGuid().ToString("N"));

    private readonly string _designMount = Path.Combine(
        Path.GetTempPath(),
        "elsa-composability-design",
        Guid.NewGuid().ToString("N"));

    public ReconciliationComposabilityTests()
    {
        Directory.CreateDirectory(_artifactMount);
        Directory.CreateDirectory(_designMount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactMount))
            Directory.Delete(_artifactMount, true);
        if (Directory.Exists(_designMount))
            Directory.Delete(_designMount, true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Either_both_or_neither_reconciler_composes_and_starts(
        bool designReconciliation,
        bool artifactReconciliation)
    {
        // Both mounts are populated in every cell, so a cell that imports something it was not armed for fails
        // instead of passing by absence.
        await MountArtifactClosureAsync();
        MountDesignCatalog();

        await using var provider = BuildEngine(designReconciliation, artifactReconciliation);

        // The claim: this composition starts. Startup tasks resolve, order and run; background and recurring
        // tasks start. A registration collision or an unresolvable graph surfaces right here.
        await provider.GetRequiredService<ITaskManager>().StartExecutingRegisteredTasks(CancellationToken.None);

        await AssertArtifactSideAsync(provider, expectedArmed: artifactReconciliation);
        await AssertDesignSideAsync(provider, expectedArmed: designReconciliation);
    }

    /// <summary>The artifact side is armed exactly when its feature is composed — and inert otherwise.</summary>
    private static async Task AssertArtifactSideAsync(IServiceProvider provider, bool expectedArmed)
    {
        await using var scope = provider.CreateAsyncScope();
        var slot = await provider.GetRequiredService<IWorkflowActivationAuthority>()
            .FindAsync(ArtifactDefinitionId, "default");
        var bindings = await provider.GetRequiredService<IWorkflowTriggerBindingStore>()
            .ListAllByStimulusAsync(EventStimulus.StimulusType, EventStimulus.Hash(ArtifactEventName));

        if (!expectedArmed)
        {
            Assert.Null(scope.ServiceProvider.GetService<IWorkflowArtifactReconciler>());
            Assert.Null(slot);
            Assert.Empty(bindings);
            return;
        }

        Assert.NotNull(scope.ServiceProvider.GetService<IWorkflowArtifactReconciler>());
        Assert.NotNull(slot);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, slot!.Source!.Kind);
        var binding = Assert.Single(bindings);
        Assert.Equal(slot.ActiveActivationId, binding.ActivationId);
    }

    /// <summary>The design side is armed exactly when its feature is composed — and inert otherwise.</summary>
    private static async Task AssertDesignSideAsync(IServiceProvider provider, bool expectedArmed)
    {
        await using var scope = provider.CreateAsyncScope();
        var catalog = provider.GetRequiredService<InMemoryDesignCatalog>();

        if (!expectedArmed)
        {
            Assert.Null(scope.ServiceProvider.GetService<IWorkflowVersionReconciler>());
            Assert.Empty(catalog.Definitions);
            Assert.Empty(catalog.Versions);
            return;
        }

        Assert.NotNull(scope.ServiceProvider.GetService<IWorkflowVersionReconciler>());
        var definition = Assert.Single(catalog.Definitions);
        Assert.Equal(DesignDefinitionId, definition.Id);
        var version = Assert.Single(catalog.Versions);
        Assert.Equal(DesignDefinitionId, version.DefinitionId);
        Assert.Equal("1.0.0", version.Version);
    }

    /// <summary>
    /// Composes one cell of the matrix: the shared runtime/execution spine plus zero, one or both reconcilers.
    /// </summary>
    /// <remarks>
    /// Scope validation is on because a composability claim that only holds until something is resolved out of the
    /// wrong lifetime is not a composability claim. The design-persistence collaborators are supplied by an
    /// in-memory catalog rather than by a Groundwork provider: no in-memory design store ships in <c>src</c>, and
    /// the subject here is feature composition, not persistence.
    /// </remarks>
    private ServiceProvider BuildEngine(bool designReconciliation, bool artifactReconciliation)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        // No IDistributedLockProvider ships as a default anywhere in src — deliberately, so a multi-node host
        // cannot silently reconcile one mount twice. A host composes a locking feature; a test composes this.
        services.AddSingleton<IDistributedLockProvider, GrantingLockProvider>();

        new SerializationFeature().ConfigureServices(services);
        new TasksFeature().ConfigureServices(services);
        new EventsFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);

        // Present in every cell so that "the design side did nothing" is an observation about the reconciler
        // rather than about a missing store.
        services.AddSingleton<InMemoryDesignCatalog>();
        services.AddSingleton<IWorkflowDefinitionStore>(sp => sp.GetRequiredService<InMemoryDesignCatalog>());
        services.AddSingleton<IWorkflowDefinitionVersionStore>(sp => sp.GetRequiredService<InMemoryDesignCatalog>());
        services.AddSingleton<IMaterializeWorkflowDefinitionCommand>(sp => sp.GetRequiredService<InMemoryDesignCatalog>());
        services.AddSingleton<IMaterializeWorkflowDefinitionVersionCommand>(sp => sp.GetRequiredService<InMemoryDesignCatalog>());
        services.AddSingleton<ISaveWorkflowDefinitionCommand>(sp => sp.GetRequiredService<InMemoryDesignCatalog>());
        services.AddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.AddSingleton<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();

        if (designReconciliation)
            new JsonWorkflowReconciliationFeature
            {
                Options =
                {
                    SourceId = "design-catalog",
                    FolderPath = _designMount,
                },
            }.ConfigureServices(services);

        if (artifactReconciliation)
            new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = "mounted-artifacts",
                    FolderPath = _artifactMount,
                },
            }.ConfigureServices(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Writes a real compiled closure into the artifact mount, exported from a publish-capable engine.</summary>
    private async Task MountArtifactClosureAsync()
    {
        await using var builder = CombinedEngine.Create(
        [
            CombinedEngine.EventWorkflow(
                ArtifactDefinitionId,
                ArtifactVersionId,
                "1.0.0",
                "node-artifact-side",
                ArtifactEventName)
        ]);
        await builder.PublishAsync(ArtifactVersionId);
        await builder.ExportToAsync(ArtifactVersionId, _artifactMount, "artifact-side.json");
    }

    /// <summary>Writes a design-side reconciliation catalog into the design mount.</summary>
    private void MountDesignCatalog()
    {
        var model = new WorkflowVersionReconciliationModel(
            DesignDefinitionId,
            "Design side",
            "Mounted by the design-side JSON reconciler.",
            "1.0.0",
            new Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState([], null, [], [], null));

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        new SerializationFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        File.WriteAllText(
            Path.Combine(_designMount, "catalog.json"),
            provider.GetRequiredService<IPayloadSerializer>().Serialize(new[] { model }));
    }

    /// <summary>A working, non-throwing design catalog: the reconciler's writes have to land somewhere assertable.</summary>
    private sealed class InMemoryDesignCatalog
        : IWorkflowDefinitionStore,
            IWorkflowDefinitionVersionStore,
            IMaterializeWorkflowDefinitionCommand,
            IMaterializeWorkflowDefinitionVersionCommand,
            ISaveWorkflowDefinitionCommand
    {
        private readonly List<WorkflowDefinition> _definitions = [];
        private readonly List<WorkflowDefinitionVersion> _versions = [];

        public IReadOnlyList<WorkflowDefinition> Definitions => _definitions;

        public IReadOnlyList<WorkflowDefinitionVersion> Versions => _versions;

        Task<WorkflowDefinition> IWorkflowDefinitionStore.GetAsync(string id, CancellationToken cancellationToken) =>
            _definitions.SingleOrDefault(definition => definition.Id == id) is { } match
                ? Task.FromResult(match)
                : throw new ArgumentException($"Unknown workflow definition '{id}'.");

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_definitions.SingleOrDefault(definition => definition.Id == id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(
            WorkflowDefinitionFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_definitions
                .Where(definition => filter.Id is null || definition.Id == filter.Id)
                .ToArray());

        Task<WorkflowDefinitionVersion> IWorkflowDefinitionVersionStore.GetAsync(
            string versionId,
            CancellationToken cancellationToken) =>
            ((IWorkflowDefinitionVersionStore)this).GetWithDefinitionAsync(versionId, cancellationToken);

        Task<WorkflowDefinitionVersion?> IWorkflowDefinitionVersionStore.FindByIdAsync(
            string versionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_versions.SingleOrDefault(version => version.Id == versionId));

        Task<WorkflowDefinitionVersion> IWorkflowDefinitionVersionStore.GetWithDefinitionAsync(
            string versionId,
            CancellationToken cancellationToken) =>
            _versions.SingleOrDefault(version => version.Id == versionId) is { } match
                ? Task.FromResult(match)
                : throw new ArgumentException($"Unknown workflow version '{versionId}'.");

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions
                .Where(version => version.DefinitionId == definitionId)
                .OrderBy(version => version.SemVerSortKey, StringComparer.Ordinal)
                .LastOrDefault());

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(_versions
                .Where(version => version.DefinitionId == definitionId)
                .ToArray());

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.Any(version =>
                version.DefinitionId == definitionId && version.SemVerSortKey == semVerSortKey));

        public Task<string> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
        {
            _definitions.RemoveAll(existing => existing.Id == definition.Id);
            _definitions.Add(definition);
            return Task.FromResult(definition.Id);
        }

        public Task<WorkflowDefinitionVersionAdded> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinitionVersion version,
            CancellationToken cancellationToken = default)
        {
            _versions.Add(version);
            return Task.FromResult(new WorkflowDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version));
        }

        Task ISaveWorkflowDefinitionCommand.Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition definition,
            CancellationToken cancellationToken)
        {
            _definitions.RemoveAll(existing => existing.Id == definition.Id);
            _definitions.Add(definition);
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _ordinal;

        public string Generate() => $"generated-{Interlocked.Increment(ref _ordinal)}";
    }

    /// <summary>Grants every lock: this host is the single node, which is the condition under test.</summary>
    private sealed class GrantingLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) => new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
