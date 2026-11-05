using System.Data.Common;
using System.Transactions;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class AuthenticationArtifactPersistenceTests
{
    [Fact]
    public async Task Restart_preserves_artifacts_and_wrong_browser_purpose_administrator_or_tenant_cannot_consume_them()
    {
        using var database = new OwnedDatabase();
        var item = Artifact(AuthenticationArtifactPurpose.Preview);
        await using (var node = await Create(database.File))
        {
            await using var scope = Scope(node);
            var store = scope.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
            Assert.True(store.IsDurable);
            Assert.True(await store.TryCreateAsync(item));
            Assert.False(await store.TryCreateAsync(item));
        }
        await using var restarted = await Create(database.File);
        await using var request = Scope(restarted);
        var authority = request.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        Assert.Null(await Consume(authority, item with { BrowserBinding = new('e', 64) }));
        Assert.Null(await Consume(authority, item with { AdministratorBinding = new('e', 64) }));
        Assert.Null(await Consume(authority, item with { Purpose = AuthenticationArtifactPurpose.PreviewResult }));
        await using (var foreign = Scope(restarted, "another"))
            Assert.Null(await Consume(foreign.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>(), item with { TenantId = "another" }));
        Assert.Equal(item, await Consume(authority, item));
        Assert.Null(await Consume(authority, item));
    }

    [Fact]
    public async Task Two_independent_nodes_that_both_read_the_row_acknowledge_exactly_one_consumer()
    {
        using var database = new OwnedDatabase();
        var barrier = new DeleteBarrier();
        await using var nodeA = await Create(database.File, barrier);
        await using var nodeB = await Create(database.File, barrier);
        await using var requestA = Scope(nodeA);
        await using var requestB = Scope(nodeB);
        var a = requestA.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        var b = requestB.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        var item = Artifact();
        Assert.True(await a.TryCreateAsync(item));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(Consume(a, item, timeout.Token).AsTask(), Consume(b, item, timeout.Token).AsTask());
        Assert.Equal(2, barrier.Arrivals);
        Assert.Single(results, result => result is not null);
        Assert.Single(results, result => result is null);
    }

    [Fact]
    public async Task Expiry_schema_compatibility_and_transaction_boundaries_fail_closed()
    {
        using var database = new OwnedDatabase();
        await using var node = await Create(database.File);
        await using var request = Scope(node);
        var store = request.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        var context = request.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
        var item = Artifact();
        Assert.True(await store.TryCreateAsync(item));
        Assert.Null(await store.TryConsumeAsync(item.TenantId, item.Id, item.Purpose, item.BrowserBinding, null, item.ExpiresAt));
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_authentication_artifacts SET SchemaVersion = '99.0.0' WHERE Id = {item.Id}");
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Consume(store, item).AsTask());
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_authentication_artifacts SET SchemaVersion = '1.0.0' WHERE Id = {item.Id}");
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(store, item).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryCreateAsync(item).AsTask());
            await transaction.RollbackAsync();
        }
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(store, item).AsTask());
        Assert.Equal(item, await Consume(store, item));
    }

    [Fact]
    public async Task Cleanup_is_bounded_and_preserves_other_tenants_and_future_schema_rows()
    {
        using var database = new OwnedDatabase();
        await using var node = await Create(database.File);
        await using var scope = Scope(node);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        var expired = Artifact() with { CreatedAt = Now.AddMinutes(-10), ExpiresAt = Now.AddMinutes(-5) };
        for (var i = 0; i < 70; i++)
            Assert.True(await store.TryCreateAsync(expired with { Id = i.ToString("x64") }));
        await using (var other = Scope(node, "another"))
            Assert.True(await other.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>().TryCreateAsync(expired with { TenantId = "another", Id = new('f', 64) }));
        var context = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity_authentication_artifacts SET SchemaVersion = '99.0.0' WHERE Id = {new string('0', 64)}");
        Assert.True(await store.TryCreateAsync(Artifact()));
        // 64 current-schema rows were removed; six plus the foreign row remain, then the new artifact.
        await using var command = context.Database.GetDbConnection().CreateCommand();
        await context.Database.OpenConnectionAsync();
        command.CommandText = "SELECT COUNT(*) FROM identity_authentication_artifacts";
        Assert.Equal(8L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Scope_and_envelope_validation_happen_before_any_write()
    {
        using var database = new OwnedDatabase();
        await using var node = await Create(database.File);
        await using var scope = Scope(node, "another");
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => store.TryCreateAsync(Artifact()).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCreateAsync(Artifact() with { Id = "raw-state-handle" }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCreateAsync(Artifact() with { TenantId = "another", ProtectedPayload = new('x', 196609) }).AsTask());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("consume-read")]
    [InlineData("consume-delete")]
    public async Task Provider_failures_cross_the_contract_as_domain_failures_and_cancellation_is_preserved(string operation)
    {
        using var database = new OwnedDatabase();
        var faults = new FailureCommands();
        await using var node = await Create(database.File, faults);
        await using var scope = Scope(node);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        var item = Artifact();
        Assert.True(await store.TryCreateAsync(item));
        faults.DeleteOnly = operation == "consume-delete";
        var infrastructure = new OwnedProviderException(); faults.Failure = infrastructure;
        Func<Task> call = operation == "create" ? () => store.TryCreateAsync(item).AsTask() : () => Consume(store, item).AsTask();
        var failure = await Assert.ThrowsAsync<AuthenticationStoreException>(call);
        Assert.Same(infrastructure, failure.InnerException);
        Assert.Equal(operation == "create" ? "create-artifact" : "consume-artifact", failure.Operation);
        Assert.Equal(item.TenantId, failure.TenantId); Assert.Equal(item.Id, failure.RecordId);
        var cancellation = new OperationCanceledException(new CancellationToken(true)); faults.Failure = cancellation;
        Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(call));
        faults.Failure = null;
        Assert.Equal(item, await Consume(store, item));
    }

    private sealed class OwnedProviderException() : DbException("Owned provider failure.");
    private sealed class FailureCommands : DbCommandInterceptor
    {
        public Exception? Failure;
        public bool DeleteOnly;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!DeleteOnly && Failure is not null) throw Failure;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private static AuthenticationArtifact Artifact(AuthenticationArtifactPurpose purpose = AuthenticationArtifactPurpose.SignIn) =>
        new(new('a', 64), "default", purpose, new('b', 64), purpose == AuthenticationArtifactPurpose.SignIn ? null : new('c', 64), Now, Now.AddMinutes(5), "protected-payload");
    private static ValueTask<AuthenticationArtifact?> Consume(IAuthenticationArtifactStore store, AuthenticationArtifact item, CancellationToken token = default) =>
        store.TryConsumeAsync(item.TenantId, item.Id, item.Purpose, item.BrowserBinding, item.AdministratorBinding, Now, token);
    private static AsyncServiceScope Scope(ServiceProvider provider, string tenant = "default")
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        return scope;
    }
    private static async Task<ServiceProvider> Create(string file, DbCommandInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = $"Data Source={file};Pooling=False" });
        if (interceptor is not null) services.AddDbContext<ExternalAuthenticationSqliteDbContext>((_, builder) => builder.AddInterceptors(interceptor));
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.MigrateAsync();
        return provider;
    }
    private sealed class DeleteBarrier : DbCommandInterceptor
    {
        public int Arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"identity_authentication_artifacts\"", StringComparison.Ordinal) && command.CommandText.Contains("Version", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref Arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    private sealed class OwnedDatabase : IDisposable
    {
        public string File { get; } = Path.Combine(Path.GetTempPath(), $"elsa-3579-artifacts-{Guid.NewGuid():n}.db");
        public void Dispose() { foreach (var suffix in new[] { "", "-wal", "-shm" }) System.IO.File.Delete(File + suffix); }
    }
}
