using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Extensions;
using Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Connections.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Tests;

public sealed class ConnectionManagerRollbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_cleanup_respects_the_actual_committed_version_and_references(bool commitBeforeFailure)
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-rollback-" + Guid.NewGuid().ToString("N") + ".db");
        var cause = new IOException("controlled save failure");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "connections-rollback-fixture-key"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSecrets(configuration);
            services.AddConnections(configuration);
            services.AddConnectionsEntityFrameworkCore(new() { ConnectionString = "Data Source=" + path + ";Pooling=False" });
            services.RemoveAll<IConnectionRepository>();
            services.AddScoped<IConnectionRepository>(provider => new FailedSave(
                new EfConnectionRepository(provider.GetRequiredService<ConnectionsDbContext>()), commitBeforeFailure, cause));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ConnectionsDbContext>();
            await context.Database.MigrateAsync();
            var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
            var failure = await Assert.ThrowsAsync<ConnectionStoreException>(async () => await manager.CreateAsync("tenant", ConnectionsLifecycleTests.Request()));
            Assert.Same(cause, failure.InnerException);
            // Read through a separate context, so tracked entities cannot hide what the rollback kept.
            await using var verification = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>()
                .UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            var repository = new EfConnectionRepository(verification);
            var document = await repository.FindAsync("tenant", "mail");
            var secret = await scope.ServiceProvider.GetRequiredService<ISecretManager>().FindAsync("tenant", "connection:mail:token");
            Assert.Equal(commitBeforeFailure, document is not null);
            Assert.Equal(commitBeforeFailure, secret is not null);
            Assert.Equal(commitBeforeFailure, await repository.IsSecretReferencedAsync("tenant", "connection:mail:token"));
            if (commitBeforeFailure)
            {
                Assert.Equal(1, document!.State.Version);
                await using var resolved = await scope.ServiceProvider.GetRequiredService<IConnectionResolver>().ResolveAsync(new()
                {
                    TenantId = "tenant", Name = "mail", RequiredType = ConnectionTypeNames.Http
                });
                Assert.Equal("fixture-only-token", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
            }
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }

    private sealed class FailedSave(IConnectionRepository inner, bool commit, Exception cause) : IConnectionRepository
    {
        public ValueTask<ConnectionRepositoryCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => inner.GetCapabilitiesAsync(cancellationToken);
        public ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default) => inner.FindAsync(tenantId, name, cancellationToken);
        public ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default) => inner.ListAsync(tenantId, query, cancellationToken);
        public ValueTask<bool> IsSecretReferencedAsync(string tenantId, string secretName, string? excludedConnectionName = null, CancellationToken cancellationToken = default) => inner.IsSecretReferencedAsync(tenantId, secretName, excludedConnectionName, cancellationToken);
        public ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default) => inner.TryAddAsync(document, cancellationToken);
        public async ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
        {
            if (commit) await inner.SaveAsync(document, cancellationToken);
            throw cause;
        }
        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) => inner.DeleteAsync(tenantId, name, cancellationToken);
        public ValueTask<bool> DeleteAsync(string tenantId, string name, int expectedVersion, CancellationToken cancellationToken = default) => inner.DeleteAsync(tenantId, name, expectedVersion, cancellationToken);
    }
}
