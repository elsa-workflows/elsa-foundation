using System.Reflection;
using System.Text.Json;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeStoreRegistrationTests
{
    private const string CacheTypeName = "Elsa.Workflows.Runtime.Core.Services.CachingWorkflowExecutableStore";

    [Fact]
    public void OriginalParameterlessRegistrationOverload_IsPreservedWithDirectProviderBehavior()
    {
        var method = typeof(GroundworkRuntimeStoreRegistration).GetMethod(
            nameof(GroundworkRuntimeStoreRegistration.AddGroundworkRuntimeStores),
            [typeof(IServiceCollection)]);

        Assert.NotNull(method);
        var services = NewServices();
        services.AddGroundworkRuntimeStores();
        using var provider = services.BuildServiceProvider();
        Assert.IsType<GroundworkWorkflowExecutableStore>(provider.GetRequiredService<IWorkflowExecutableStore>());
    }

    [Fact]
    public void ExplicitRegistration_RejectsNullOptions()
    {
        var services = NewServices();

        Assert.Throws<ArgumentNullException>(() => services.AddGroundworkRuntimeStores(null!));
    }

    [Fact]
    public void HostSuppliedKeyedProvider_IsPreservedAndDecorated()
    {
        var services = NewServices();
        var replacement = new ReplacementWorkflowExecutableStore();
        services.AddKeyedSingleton<IWorkflowExecutableStore>(
            GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey,
            replacement);

        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions());

        using var provider = services.BuildServiceProvider();
        var selected = provider.GetRequiredService<IWorkflowExecutableStore>();
        Assert.Same(replacement, provider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey));
        Assert.Contains(
            selected.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => ReferenceEquals(field.GetValue(selected), replacement));
    }

    [Fact]
    public void DefaultRegistration_SelectsCacheAroundConcreteGroundworkStore()
    {
        var services = NewServices();

        services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions());

        using var provider = services.BuildServiceProvider();
        var selected = provider.GetRequiredService<IWorkflowExecutableStore>();
        var durableProvider = provider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey);
        Assert.Equal(CacheTypeName, selected.GetType().FullName);
        Assert.IsType<GroundworkWorkflowExecutableStore>(durableProvider);
        Assert.Contains(
            selected.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => ReferenceEquals(field.GetValue(selected), durableProvider));
    }

    [Fact]
    public void DisabledRegistration_SelectsConcreteProviderDirectly_AndIgnoresCapacity()
    {
        var services = NewServices();

        AddGroundworkRuntimeStores(services, enabled: false, capacity: 0);

        using var provider = services.BuildServiceProvider();
        var selected = provider.GetRequiredService<IWorkflowExecutableStore>();
        var durableProvider = provider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey);
        Assert.IsType<GroundworkWorkflowExecutableStore>(selected);
        Assert.Same(selected, durableProvider);
    }

    [Fact]
    public void EnabledRegistration_AppliesConfiguredCapacity()
    {
        var services = NewServices();

        AddGroundworkRuntimeStores(services, enabled: true, capacity: 17);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(17, provider.GetRequiredService<WorkflowExecutableCacheOptions>().Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnabledRegistration_RejectsNonPositiveCapacity(int capacity)
    {
        var services = NewServices();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AddGroundworkRuntimeStores(services, enabled: true, capacity: capacity));
        Assert.Contains("capacity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RebuiltProvider_StartsWithEmptyCache_AndLoadsFromDurableAuthorityAgain()
    {
        var documents = new CountingDocumentStore(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        var executable = Executable();

        object firstCache;
        await using (var firstProvider = BuildProvider(documents))
        {
            await firstProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
                GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey).SaveAsync(executable);
            documents.ResetLoadCount();

            firstCache = firstProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.Equal(CacheTypeName, firstCache.GetType().FullName);
            Assert.NotNull(await ((IWorkflowExecutableStore)firstCache).FindAsync(executable.Identity.ArtifactId));
            Assert.NotNull(await ((IWorkflowExecutableStore)firstCache).FindAsync(executable.Identity.ArtifactId));
            Assert.Equal(1, documents.LoadCount);
        }

        await using (var secondProvider = BuildProvider(documents))
        {
            var secondCache = secondProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.NotSame(firstCache, secondCache);
            Assert.NotNull(await secondCache.FindAsync(executable.Identity.ArtifactId));
            Assert.Equal(2, documents.LoadCount);
        }
    }

    [Fact]
    public async Task ProviderBackedCache_CoversSaveFindDeleteConcurrencyAndEviction()
    {
        var documents = new CountingDocumentStore(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        var first = Executable("artifact-provider-a");
        var second = Executable("artifact-provider-b");

        await using var provider = BuildProvider(
            documents,
            new WorkflowExecutableCacheOptions { Capacity = 1 });
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        documents.ResetLoadCount();

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => store.FindAsync(first.Identity.ArtifactId).AsTask()));
        Assert.NotNull(concurrent[0]);
        Assert.All(concurrent, executable => Assert.Same(concurrent[0], executable));
        Assert.Equal(first.Identity, concurrent[0]!.Identity);
        Assert.Equal(1, documents.LoadCount);

        Assert.Equal(second.Identity, (await store.FindAsync(second.Identity.ArtifactId))!.Identity);
        Assert.Equal(first.Identity, (await store.FindAsync(first.Identity.ArtifactId))!.Identity);
        Assert.Equal(3, documents.LoadCount);

        Assert.True(await store.DeleteAsync(first.Identity.ArtifactId));
        Assert.Null(await store.FindAsync(first.Identity.ArtifactId));
        Assert.Equal(4, documents.LoadCount);
    }

    private static ServiceCollection NewServices(IDocumentStore? documentStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(documentStore ?? new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        return services;
    }

    private static ServiceProvider BuildProvider(
        IDocumentStore documents,
        WorkflowExecutableCacheOptions? options = null)
    {
        var services = NewServices(documents);
        services.AddGroundworkRuntimeStores(options ?? new WorkflowExecutableCacheOptions());
        return services.BuildServiceProvider();
    }

    private static void AddGroundworkRuntimeStores(IServiceCollection services, bool enabled, int capacity)
        => services.AddGroundworkRuntimeStores(new WorkflowExecutableCacheOptions { Enabled = enabled, Capacity = capacity });

    private static WorkflowExecutable Executable(string artifactId = "artifact-cache-restart")
    {
        var root = new ExecutableNode(
            executableNodeId: "root",
            authoredActivityId: "authored-root",
            activityType: "Elsa.Sequence",
            activityTypeVersion: "1.0.0",
            descriptorType: "Elsa.Activities.SequenceDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = "Sequence" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                ArtifactId: artifactId,
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: "1",
                ArtifactHash: $"hash-{artifactId}"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private sealed class CountingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        private int _loadCount;

        public DocumentStoreAccess Access => inner.Access;
        public int LoadCount => Volatile.Read(ref _loadCount);

        public void ResetLoadCount() => Interlocked.Exchange(ref _loadCount, 0);

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loadCount);
            return inner.LoadAsync(documentKind, id, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }

    private sealed class ReplacementWorkflowExecutableStore : IWorkflowExecutableStore
    {
        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(
            string artifactId,
            string leaseId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableRootWriteLease?>(null);

        public ValueTask<bool> RenewRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask ReleaseRootWriteLeaseAsync(
            WorkflowExecutableRootWriteLease lease,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(
            string artifactId,
            string operationId,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableDeletionGuard?>(null);

        public ValueTask<bool> CancelDeletionAsync(
            WorkflowExecutableDeletionGuard guard,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> DeleteAsync(
            WorkflowExecutableDeletionGuard guard,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutable?>(null);

        public ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutable>>([]);
    }
}
