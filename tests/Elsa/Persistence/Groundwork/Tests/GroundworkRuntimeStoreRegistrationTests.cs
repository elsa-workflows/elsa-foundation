using System.Reflection;
using System.Text.Json;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeStoreRegistrationTests
{
    private const string CacheTypeName = "Elsa.Workflows.Runtime.Core.Services.CachingWorkflowExecutableStore";
    private const string OptionsTypeName = "Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableCacheOptions";

    [Fact]
    public void DefaultRegistration_SelectsCacheAroundConcreteGroundworkStore()
    {
        var services = NewServices();

        services.AddGroundworkRuntimeStores();

        using var provider = services.BuildServiceProvider();
        var selected = provider.GetRequiredService<IWorkflowExecutableStore>();
        var concrete = provider.GetRequiredService<GroundworkWorkflowExecutableStore>();
        Assert.Equal(CacheTypeName, selected.GetType().FullName);
        Assert.Contains(
            selected.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => ReferenceEquals(field.GetValue(selected), concrete));
    }

    [Fact]
    public void DisabledRegistration_SelectsConcreteProviderDirectly_AndIgnoresCapacity()
    {
        var services = NewServices();

        AddGroundworkRuntimeStores(services, enabled: false, capacity: 0);

        using var provider = services.BuildServiceProvider();
        var selected = provider.GetRequiredService<IWorkflowExecutableStore>();
        Assert.IsType<GroundworkWorkflowExecutableStore>(selected);
        Assert.Same(selected, provider.GetRequiredService<GroundworkWorkflowExecutableStore>());
    }

    [Fact]
    public void EnabledRegistration_AppliesConfiguredCapacity()
    {
        var services = NewServices();

        AddGroundworkRuntimeStores(services, enabled: true, capacity: 17);

        using var provider = services.BuildServiceProvider();
        var options = ResolveCacheOptions(provider);
        Assert.Equal(17, ReadIntProperty(options, "Capacity"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnabledRegistration_RejectsNonPositiveCapacity(int capacity)
    {
        var services = NewServices();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            AddGroundworkRuntimeStores(services, enabled: true, capacity: capacity));

        Assert.NotNull(exception.InnerException);
        Assert.Contains("capacity", exception.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RebuiltProvider_StartsWithEmptyCache_AndLoadsFromDurableAuthorityAgain()
    {
        var documents = new CountingDocumentStore(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        var executable = Executable();

        object firstCache;
        await using (var firstProvider = BuildProvider(documents))
        {
            await firstProvider.GetRequiredService<GroundworkWorkflowExecutableStore>().SaveAsync(executable);
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

    private static ServiceCollection NewServices(IDocumentStore? documentStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(documentStore ?? new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()));
        return services;
    }

    private static ServiceProvider BuildProvider(IDocumentStore documents)
    {
        var services = NewServices(documents);
        services.AddGroundworkRuntimeStores();
        return services.BuildServiceProvider();
    }

    private static void AddGroundworkRuntimeStores(IServiceCollection services, bool enabled, int capacity)
    {
        var method = typeof(GroundworkRuntimeStoreRegistration).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(candidate =>
                candidate.Name == nameof(GroundworkRuntimeStoreRegistration.AddGroundworkRuntimeStores)
                && candidate.GetParameters().Length == 3);
        Assert.NotNull(method);
        method!.Invoke(null, [services, enabled, capacity]);
    }

    internal static object ResolveCacheOptions(IServiceProvider provider)
    {
        var type = typeof(IWorkflowExecutableStore).Assembly.GetType(OptionsTypeName);
        Assert.NotNull(type);
        var options = provider.GetService(type!);
        Assert.NotNull(options);
        return options!;
    }

    internal static int ReadIntProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property!.GetValue(instance));
    }

    private static WorkflowExecutable Executable()
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
                ArtifactId: "artifact-cache-restart",
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: "1",
                ArtifactHash: "hash-artifact-cache-restart"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private sealed class CountingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        public int LoadCount { get; private set; }

        public void ResetLoadCount() => LoadCount = 0;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            LoadCount++;
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
}
