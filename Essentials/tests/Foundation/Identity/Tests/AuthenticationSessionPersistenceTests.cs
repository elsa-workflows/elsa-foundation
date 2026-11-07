using System.Data.Common;
using System.Transactions;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class AuthenticationSessionPersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    [Fact]
    public async Task Restart_and_paging_preserve_exact_session_owners_and_protected_provenance()
    {
        using var data = new OwnedData();
        var item = Session() with { ConnectionKey = "company", ConnectionRecordId = "deployment:company", Issuer = "https://company.example/issuer", ExternalSubject = "Opaque-Subject", ExternalLinkRevision = "link-revision",
            ConnectionMaterialRevision = "opaque-material-revision", ConnectionCredentialBinding = "opaque-credential-binding", ConnectionRedirectUri = "https://backend.example/signin/company" };
        await using (var node = await Node(data))
        {
            await using var scope = Scope(node);
            var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
            Assert.True(store.IsDurable);
            Assert.True(await store.TryCreateAsync(item));
            Assert.False(await store.TryCreateAsync(item));
            Assert.True(await store.TryCreateAsync(item with { Id = new('b', 64), UserId = "another-user" }));
            Assert.True(await store.TryCreateAsync(item with { Id = new('c', 64), CreatedAt = Now.AddMilliseconds(1) }));
            await using var foreign = Scope(node, "other-tenant");
            Assert.True(await foreign.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>().TryCreateAsync(item with { TenantId = "other-tenant" }));
        }
        await using var restarted = await Node(data);
        await using var request = Scope(restarted);
        var authority = request.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        var restored = await authority.FindAsync(item.TenantId, item.Id);
        Assert.NotNull(restored); Assert.Equal(item, restored with { Revision = null }); Assert.Equal(64, restored.Revision!.Length);
        var first = await authority.ListAsync(item.TenantId, item.UserId, new(0, 1));
        Assert.Equal(2, first.TotalCount); Assert.Equal(new string('c', 64), Assert.Single(first.Items).Id);
        var second = await authority.ListAsync(item.TenantId, item.UserId, new(1, 1));
        Assert.Equal(item.Id, Assert.Single(second.Items).Id);
        Assert.Empty((await authority.ListAsync(item.TenantId, "unknown", new())).Items);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => authority.FindAsync("other-tenant", item.Id).AsTask());
        await using var foreignScope = Scope(restarted, "other-tenant");
        var foreignAuthority = foreignScope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        Assert.Equal("other-tenant", (await foreignAuthority.FindAsync("other-tenant", item.Id))!.TenantId);
        Assert.Null(await foreignAuthority.FindAsync("other-tenant", new('b', 64)));
        var context = request.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        await context.Database.OpenConnectionAsync();
        command.CommandText = "SELECT ProtectedPayload FROM identity_authentication_sessions LIMIT 1";
        var payload = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.DoesNotContain(item.SecurityVersion, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Opaque-Subject", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(item.ConnectionCredentialBinding!, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(item.ConnectionRedirectUri!, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selected_revocation_is_revisioned_committed_idempotent_and_preserves_other_sessions()
    {
        using var data = new OwnedData();
        await using var first = await Node(data); await using var second = await Node(data);
        await using var scope = Scope(first); await using var other = Scope(second);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        var remote = other.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        Assert.True(await store.TryCreateAsync(Session())); Assert.True(await store.TryCreateAsync(Session() with { Id = new('b', 64) }));
        var current = (await remote.FindAsync("default", new('a', 64)))!;
        Assert.Equal(AuthenticationSessionRevocationStatus.Conflict, await store.RevokeAsync("default", current.Id, new('0', 64), Now));
        Assert.Null((await remote.FindAsync("default", current.Id))!.RevokedAt);
        Assert.Equal(AuthenticationSessionRevocationStatus.Revoked, await store.RevokeAsync("default", current.Id, current.Revision!, Now));
        Assert.Equal(Now, (await remote.FindAsync("default", current.Id))!.RevokedAt);
        Assert.Equal(AuthenticationSessionRevocationStatus.AlreadyRevoked, await remote.RevokeAsync("default", current.Id, current.Revision!, Now));
        Assert.Null((await remote.FindAsync("default", new('b', 64)))!.RevokedAt);
        Assert.Equal(AuthenticationSessionRevocationStatus.Missing, await remote.RevokeAsync("default", new('f', 64), current.Revision!, Now));
    }

    [Fact]
    public async Task Two_nodes_cannot_both_acknowledge_one_revision_and_a_provider_failure_stays_retryable()
    {
        using var data = new OwnedData();
        var barrier = new RevokeBarrier();
        await using var first = await Node(data, barrier); await using var second = await Node(data, barrier);
        await using var scope = Scope(first); await using var other = Scope(second);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        var remote = other.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        Assert.True(await store.TryCreateAsync(Session()));
        var current = (await store.FindAsync("default", new('a', 64)))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var results = await Task.WhenAll(store.RevokeAsync("default", current.Id, current.Revision!, Now, timeout.Token).AsTask(),
            remote.RevokeAsync("default", current.Id, current.Revision!, Now, timeout.Token).AsTask());
        Assert.Equal(2, barrier.Arrivals); Assert.Single(results, value => value == AuthenticationSessionRevocationStatus.Revoked);
        Assert.Single(results, value => value == AuthenticationSessionRevocationStatus.Conflict);

        barrier.FailNext = true;
        Assert.True(await store.TryCreateAsync(Session() with { Id = new('b', 64) }));
        var retry = (await store.FindAsync("default", new('b', 64)))!;
        var refusal = await Assert.ThrowsAsync<AuthenticationStoreException>(() => store.RevokeAsync("default", retry.Id, retry.Revision!, Now).AsTask());
        Assert.IsType<InvalidOperationException>(refusal.InnerException);
        Assert.Null((await remote.FindAsync("default", retry.Id))!.RevokedAt);
        Assert.Equal(AuthenticationSessionRevocationStatus.Revoked, await remote.RevokeAsync("default", retry.Id, retry.Revision!, Now));
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("payload")]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("schema")]
    public async Task Corrupted_or_unreadable_session_data_never_becomes_an_authorized_session(string scenario)
    {
        using var data = new OwnedData();
        await using var node = await Node(data); await using var scope = Scope(node);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        Assert.True(await store.TryCreateAsync(Session()));
        var context = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
        if (scenario == "wrong-key")
        {
            await using var wrong = await Node(data, keyPurpose: "wrong-application"); await using var foreign = Scope(wrong);
            var failure = await Assert.ThrowsAsync<AuthenticationStoreException>(() => foreign.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>().FindAsync("default", new('a', 64)).AsTask());
            Assert.IsType<System.Security.Cryptography.CryptographicException>(failure.InnerException);
            return;
        }
        var sql = scenario switch
        {
            "payload" => "UPDATE identity_authentication_sessions SET ProtectedPayload = 'tampered'",
            "owner" => "UPDATE identity_authentication_sessions SET UserKey = 'other-owner'",
            "expiry" => "UPDATE identity_authentication_sessions SET ExpiresAtMilliseconds = ExpiresAtMilliseconds + 1",
            _ => "UPDATE identity_authentication_sessions SET SchemaVersion = '99.0.0'"
        };
        await context.Database.ExecuteSqlRawAsync(sql);
        if (scenario == "schema") await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.FindAsync("default", new('a', 64)).AsTask());
        else await Assert.ThrowsAnyAsync<InvalidOperationException>(() => store.FindAsync("default", new('a', 64)).AsTask());
    }

    [Fact]
    public async Task Ambient_transactions_invalid_envelopes_and_oversized_pages_are_refused_before_acknowledgment()
    {
        using var data = new OwnedData(); await using var node = await Node(data); await using var scope = Scope(node);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCreateAsync(Session() with { RevokedAt = Now }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCreateAsync(Session() with { ExpiresAt = Now.AddDays(31) }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCreateAsync(Session() with { ConnectionKey = "unbound" }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.TryCreateAsync(Session() with { ConnectionMaterialRevision = "unbound" }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListAsync("default", "user", new(0, 101)).AsTask());
        Assert.True(await store.TryCreateAsync(Session()));
        var current = (await store.FindAsync("default", new('a', 64)))!;
        var context = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryCreateAsync(Session() with { Id = new('b', 64) }).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RevokeAsync("default", current.Id, current.Revision!, Now).AsTask());
            await transaction.RollbackAsync();
        }
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RevokeAsync("default", current.Id, current.Revision!, Now).AsTask());
        Assert.Null((await store.FindAsync("default", current.Id))!.RevokedAt);
    }

    [Fact]
    public async Task Incremental_schema_upgrade_preserves_a_preceding_one_time_flow()
    {
        using var data = new OwnedData(); await using var node = await Node(data, migrate: false); await using var scope = Scope(node);
        var context = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
        var migrator = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        var initial = context.Database.GetMigrations().First();
        await migrator.MigrateAsync(initial);
        var artifacts = scope.ServiceProvider.GetRequiredService<IAuthenticationArtifactStore>();
        var item = new AuthenticationArtifact(new('d', 64), "default", AuthenticationArtifactPurpose.SignIn, new('e', 64), null, Now, Now.AddMinutes(5), "old-protected-payload");
        Assert.True(await artifacts.TryCreateAsync(item));
        await context.Database.ExecuteSqlRawAsync("UPDATE identity_authentication_artifacts SET SchemaVersion = '1.0.0'");
        await migrator.MigrateAsync();
        Assert.Equal(item, await artifacts.TryConsumeAsync("default", item.Id, item.Purpose, item.BrowserBinding, null, Now));
        Assert.True(await scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>().TryCreateAsync(Session()));
    }

    [Theory]
    [InlineData("create-session")]
    [InlineData("find-session")]
    [InlineData("list-sessions")]
    [InlineData("revoke-session")]
    public async Task Provider_failures_are_localized_domain_failures_and_do_not_wrap_cancellation(string operation)
    {
        using var data = new OwnedData();
        var faults = new FailureCommands();
        await using var node = await Node(data, faults);
        await using var scope = Scope(node);
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        var item = Session(); Assert.True(await store.TryCreateAsync(item));
        var current = (await store.FindAsync(item.TenantId, item.Id))!;
        var infrastructure = new OwnedProviderException(); faults.Failure = infrastructure;
        Func<Task> call = operation switch
        {
            "create-session" => () => store.TryCreateAsync(item).AsTask(),
            "find-session" => () => store.FindAsync(item.TenantId, item.Id).AsTask(),
            "list-sessions" => () => store.ListAsync(item.TenantId, item.UserId, new(0, 25)).AsTask(),
            _ => () => store.RevokeAsync(item.TenantId, item.Id, current.Revision!, Now).AsTask()
        };
        var failure = await Assert.ThrowsAsync<AuthenticationStoreException>(call);
        Assert.Same(infrastructure, failure.InnerException); Assert.Equal(operation, failure.Operation);
        Assert.Equal(item.TenantId, failure.TenantId); Assert.Equal(operation == "list-sessions" ? null : item.Id, failure.RecordId);
        var cancellation = new OperationCanceledException(new CancellationToken(true)); faults.Failure = cancellation;
        Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(call));
        faults.Failure = null;
        Assert.Null((await store.FindAsync(item.TenantId, item.Id))!.RevokedAt);
    }

    private sealed class OwnedProviderException() : DbException("Owned provider failure.");
    private sealed class FailureCommands : DbCommandInterceptor
    {
        public Exception? Failure;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static AuthenticationSessionRecord Session() => new(new('a', 64), "default", "user", "external:company", "opaque-security-version", Now, Now.AddHours(8));
    private static AsyncServiceScope Scope(ServiceProvider node, string tenant = "default")
    {
        var scope = node.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        return scope;
    }
    private static async Task<ServiceProvider> Node(OwnedData data, DbCommandInterceptor? interceptor = null, string keyPurpose = "elsa-3579-session-test", bool migrate = true)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data.Root, "keys"))).SetApplicationName(keyPurpose);
        services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = $"Data Source={data.File};Pooling=False" });
        if (interceptor is not null) services.AddDbContext<ExternalAuthenticationSqliteDbContext>((_, builder) => builder.AddInterceptors(interceptor));
        var provider = services.BuildServiceProvider();
        if (migrate) { await using var scope = provider.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.MigrateAsync(); }
        return provider;
    }
    private sealed class RevokeBarrier : DbCommandInterceptor
    {
        public int Arrivals;
        public bool FailNext;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.StartsWith("UPDATE \"identity_authentication_sessions\"", StringComparison.Ordinal)) return result;
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Owned session write refusal."); }
            if (Interlocked.Increment(ref Arrivals) <= 2)
            { if (Arrivals == 2) ready.TrySetResult(); await ready.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
    private sealed class OwnedData : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "elsa-3579-sessions-" + Guid.NewGuid().ToString("n"));
        public string File => Path.Combine(Root, "sessions.db");
        public OwnedData() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
