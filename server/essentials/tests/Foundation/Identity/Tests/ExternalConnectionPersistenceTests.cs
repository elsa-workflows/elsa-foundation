using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using System.Security.Claims;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ExternalConnectionPersistenceTests
{
    [Fact]
    public async Task Independent_node_writers_cannot_disable_both_paths_in_the_shared_provider_catalog()
    {
        var file = Path.Combine(Path.GetTempPath(), $"elsa-3579-catalog-{Guid.NewGuid():n}.db");
        try
        {
            await using var first = await Create(file);
            await using var second = await Create(file);
            var a = new ConnectionRecord { Id = "a", Key = "a", Enabled = true };
            var b = new ConnectionRecord { Id = "b", Key = "b", Enabled = true };
            var firstCatalog = first.GetRequiredService<IConnectionCatalogStore>();
            var secondCatalog = second.GetRequiredService<IConnectionCatalogStore>();
            var seeded = await firstCatalog.SaveAsync(new([a, b]));
            Assert.True(seeded.Saved);
            var barrier = new ReadBarrier();
            await using var nodeA = WriterProvider(new SynchronizedCatalog(firstCatalog, barrier));
            await using var nodeB = WriterProvider(new SynchronizedCatalog(secondCatalog, barrier));
            var results = await Task.WhenAll(
                nodeA.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Disable, seeded.Revision, a), Administrator()).AsTask(),
                nodeB.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Disable, seeded.Revision, b), Administrator()).AsTask());
            Assert.Single(results, result => result.Saved);
            Assert.Equal("revision-conflict", Assert.Single(results, result => !result.Saved).Failure);
            var current = await secondCatalog.ReadAsync();
            var remaining = Assert.Single(current.Records, record => record.Enabled);
            var retry = await nodeB.GetRequiredService<IConnectionWriter>().ApplyAsync(
                new(ConnectionChange.Disable, current.Revision, remaining), Administrator());
            Assert.Equal("final-login-path", retry.Failure);
            Assert.Single((await firstCatalog.ReadAsync()).Records, record => record.Enabled);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(file + suffix);
        }
    }

    [Fact]
    public async Task Catalog_is_durable_uses_shared_revision_CAS_and_never_rebinds_the_callers_tenant_scope()
    {
        var file = Path.Combine(Path.GetTempPath(), $"elsa-3579-catalog-{Guid.NewGuid():n}.db");
        try
        {
            var document = new ConnectionRecord
            {
                Id = "db-record", Key = "company", DisplayName = "Company",
                Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("oidc-client", "text", "identity/external") }
            };
            string? savedRevision;
            await using (var first = await Create(file))
            {
                await using var request = first.CreateAsyncScope();
                var binder = request.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>();
                binder.Bind(PersistenceAccessContext.Scoped(new PersistenceScope("caller-tenant")));
                var catalog = request.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
                var initial = await catalog.ReadAsync();
                Assert.Null(initial.Revision);
                var saved = await catalog.SaveAsync(new([document], initial.Revision));
                Assert.True(saved.Saved);
                savedRevision = saved.Revision;
                Assert.False((await catalog.SaveAsync(new([document with { DisplayName = "stale" }], initial.Revision))).Saved);
                Assert.Equal(new PersistenceScope("caller-tenant"), request.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current.Scope);
            }
            await using var restarted = await Create(file);
            var store = restarted.GetRequiredService<IConnectionCatalogStore>();
            var restored = await store.ReadAsync();
            Assert.Equal(savedRevision, restored.Revision);
            var record = Assert.Single(restored.Records);
            Assert.Equal("Company", record.DisplayName);
            Assert.Equal(document.Secrets["clientSecret"], record.Secrets["clientSecret"]);
            // A second node has the same expected revision. Only the first complete-catalog write wins.
            await using var sibling = await Create(file);
            var other = sibling.GetRequiredService<IConnectionCatalogStore>();
            var sameRevision = await other.ReadAsync();
            Assert.True((await store.SaveAsync(restored with { Records = [record with { DisplayName = "fresh" }] })).Saved);
            Assert.False((await other.SaveAsync(sameRevision with { Records = [] })).Saved);
            Assert.Equal("fresh", Assert.Single((await other.ReadAsync()).Records).DisplayName);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(file + suffix);
        }
    }

    [Fact]
    public async Task Wrong_catalog_kind_is_refused_instead_of_silently_hiding_stored_connections()
    {
        var file = Path.Combine(Path.GetTempPath(), $"elsa-3579-catalog-{Guid.NewGuid():n}.db");
        try
        {
            await using var services = await Create(file);
            await using (var scope = services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                    .Bind(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test-corrupt-catalog")));
                await scope.ServiceProvider.GetRequiredService<IProviderConfigurationStore>().SaveAsync(new(
                    "external-authentication", null, "unknown-future-kind", true, false, ProviderCapabilities.ExternalOidcDefault,
                    new Dictionary<string, string> { ["catalog"] = "[]" }));
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await services.GetRequiredService<IConnectionCatalogStore>().ReadAsync());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(file + suffix);
        }
    }

    private static ServiceProvider WriterProvider(IConnectionCatalogStore catalog)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog);
        var paths = new KnownPaths();
        services.AddSingleton<IConnectionReadiness>(paths);
        services.AddSingleton<IConnectionRecovery>(paths);
        services.AddSingleton<ILocalLoginRecovery>(paths);
        services.AddFoundationExternalAuthentication(options => options.AllowDatabaseManagement = true);
        ImmutableConnectionAuthorization.Register(services);
        services.AddNormalizedAuthenticationType("catalog-race");
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Administrator() => new(new ClaimsIdentity(
    [new Claim(IdentityClaimTypes.Normalized, "v1"), new Claim(IdentityClaimTypes.UserId, "admin"),
     new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(IdentityClaimTypes.TenantId, "default"),
     new Claim(IdentityClaimTypes.Permission, DefaultIdentityPermissionKeys.IdentityProvidersManage)], "catalog-race"));

    private sealed class KnownPaths : IConnectionReadiness, IConnectionRecovery, ILocalLoginRecovery
    {
        public ValueTask<bool> IsReadyAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> IsUsableAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> IsUsableAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ConnectionRecord connection, ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(connection.Key is "a" or "b" ? [] : null);
        public ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(null);
    }

    private sealed class ReadBarrier
    {
        private int reads;
        private readonly TaskCompletionSource bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Arrive(CancellationToken cancellationToken)
        {
            var arrived = Interlocked.Increment(ref reads);
            if (arrived <= 2)
            {
                if (arrived == 2) bothRead.TrySetResult();
                await bothRead.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class SynchronizedCatalog(IConnectionCatalogStore inner, ReadBarrier barrier) : IConnectionCatalogStore
    {
        public bool SupportsIdentityRevisionGuards => true;
        public async ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await inner.ReadAsync(cancellationToken);
            await barrier.Arrive(cancellationToken);
            return snapshot;
        }
        public ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default) => inner.SaveAsync(catalog with { IdentityRevisionGuards = null }, cancellationToken);
    }

    private static async Task<ServiceProvider> Create(string file)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationExternalAuthentication(options => options.AllowDatabaseManagement = true);
        services.AddIdentityProviderConfigurationEntityFrameworkCore(new IdentityProviderConfigurationEntityFrameworkCoreOptions
        { Provider = "Sqlite", ConnectionString = $"Data Source={file};Pooling=False" });
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().Database.EnsureCreatedAsync();
        return provider;
    }
}
