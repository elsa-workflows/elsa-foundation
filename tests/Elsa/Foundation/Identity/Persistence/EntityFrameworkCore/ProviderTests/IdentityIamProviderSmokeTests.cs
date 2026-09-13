using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(IdentityProviderPostgreSqlFixture.CollectionName)]
public sealed class IdentityIamPostgreSqlSmokeTests(IdentityProviderPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task Crud_readback_rollback_and_revision_semantics_on_postgresql() =>
        IdentityIamProviderSmoke.RunAsync(fixture, "PostgreSql");
}

[Collection(IdentityProviderSqlServerFixture.CollectionName)]
public sealed class IdentityIamSqlServerSmokeTests(IdentityProviderSqlServerFixture fixture)
{
    [SkippableFact]
    public Task Crud_readback_rollback_and_revision_semantics_on_sql_server() =>
        IdentityIamProviderSmoke.RunAsync(fixture, "SqlServer");
}

[Collection(IdentityProviderMySqlFixture.CollectionName)]
public sealed class IdentityIamMySqlSmokeTests(IdentityProviderMySqlFixture fixture)
{
    [SkippableFact]
    public Task Crud_readback_rollback_and_revision_semantics_on_mysql() =>
        IdentityIamProviderSmoke.RunAsync(fixture, "MySql");
}

internal static class IdentityIamProviderSmoke
{
    public static async Task RunAsync(IdentityProviderFixture fixture, string provider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{provider} is unavailable.");

        var tenant = $"iam-smoke-{provider}-{Guid.NewGuid():N}";
        await using var context = CreateContext(provider, fixture.ConnectionString);
        Assert.Equal(ExpectedProvider(provider), context.Database.ProviderName);
        Assert.Equal(
            new[] { typeof(ApplicationEntity), typeof(CredentialEntity) },
            context.Model.GetEntityTypes().Select(entity => entity.ClrType).OrderBy(type => type.FullName));
        await IdentityEfProviderDatabaseProvisioning.EnsureModuleTablesAsync(
            context,
            provider,
            IdentityIamEfModule.ApplicationTableName,
            IdentityIamEfModule.CredentialTableName);
        if (provider == "MySql")
            await AssertMySqlTableEncodingAsync(context);

        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var applicationStore = new EfApplicationStore(context, access);
        var credentialStore = new EfCredentialStore(context, access);
        var application = Application(tenant, "app-primary");
        var expiresAt = new DateTimeOffset(2031, 4, 5, 6, 7, 8, TimeSpan.FromHours(-7.5));
        var credential = Credential(tenant, "credential-primary", expiresAt);

        await applicationStore.SaveAsync(application);
        await credentialStore.SaveAsync(credential);

        await AssertApplicationReadbackAsync(applicationStore, application);
        await AssertCredentialReadbackAsync(credentialStore, credential, expiresAt);
        await AssertDeterministicSetsAsync(context, applicationStore, application);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await applicationStore.SaveAsync(application with { DisplayName = "rolled-back" });
            await credentialStore.SaveAsync(credential with { Status = CredentialStatus.Revoked });
            await transaction.RollbackAsync();
        }

        await using (var reopenedAfterRollback = CreateContext(provider, fixture.ConnectionString))
        {
            var reopenedAccess = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
            var reopenedApplication = await new EfApplicationStore(reopenedAfterRollback, reopenedAccess)
                .FindAsync(tenant, application.Id);
            var reopenedCredential = await new EfCredentialStore(reopenedAfterRollback, reopenedAccess)
                .FindAsync(tenant, credential.Id);
            Assert.NotNull(reopenedApplication);
            Assert.Equal(application.DisplayName, reopenedApplication!.DisplayName);
            Assert.NotNull(reopenedCredential);
            Assert.Equal(credential.Status, reopenedCredential!.Status);
            Assert.Equal(expiresAt, reopenedCredential.ExpiresAt);
        }

        await AssertCreateConflictAndCasAsync(applicationStore, credentialStore, application, credential);
        await AssertConcurrentCasAsync(fixture, provider, tenant);

        await using var reopened = CreateContext(provider, fixture.ConnectionString);
        var finalAccess = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var finalApplication = await new EfApplicationStore(reopened, finalAccess).FindAsync(tenant, application.Id);
        var finalCredential = await new EfCredentialStore(reopened, finalAccess).FindAsync(tenant, credential.Id);
        Assert.NotNull(finalApplication);
        Assert.Equal("cas-application", finalApplication!.DisplayName);
        Assert.NotNull(finalCredential);
        Assert.Equal(CredentialStatus.Rotating, finalCredential!.Status);
        Assert.Equal(expiresAt.Ticks, finalCredential.ExpiresAt!.Value.Ticks);
        Assert.Equal(expiresAt.Offset, finalCredential.ExpiresAt.Value.Offset);
    }

    private static async Task AssertApplicationReadbackAsync(
        EfApplicationStore store,
        ApplicationRecord application)
    {
        var readback = await store.FindAsync(application.TenantId, application.Id);

        Assert.NotNull(readback);
        Assert.Equal(application.Id, readback!.Id);
        Assert.Equal(application.TenantId, readback.TenantId);
        Assert.Equal(application.ClientId, readback.ClientId);
        Assert.Equal(application.DisplayName, readback.DisplayName);
        Assert.Equal(application.Type, readback.Type);
        Assert.Equal(application.Ownership, readback.Ownership);
        Assert.True(application.AllowedGrantTypes.SetEquals(readback.AllowedGrantTypes));
        Assert.True(application.Scopes.SetEquals(readback.Scopes));
    }

    private static async Task AssertCredentialReadbackAsync(
        EfCredentialStore store,
        CredentialRecord credential,
        DateTimeOffset expiresAt)
    {
        var readback = await store.FindAsync(credential.TenantId, credential.Id);

        Assert.NotNull(readback);
        Assert.Equal(credential, readback);
        Assert.Equal(expiresAt.Ticks, readback!.ExpiresAt!.Value.Ticks);
        Assert.Equal(expiresAt.Offset, readback.ExpiresAt.Value.Offset);
    }

    private static async Task AssertDeterministicSetsAsync(
        IdentityIamDbContext context,
        EfApplicationStore store,
        ApplicationRecord application)
    {
        var equivalent = application with
        {
            Id = "app-deterministic",
            AllowedGrantTypes = new HashSet<string>(application.AllowedGrantTypes.Reverse(), StringComparer.Ordinal),
            Scopes = new HashSet<string>(application.Scopes.Reverse(), StringComparer.Ordinal)
        };
        await store.SaveAsync(equivalent);

        var first = await context.Applications.AsNoTracking()
            .SingleAsync(entity => entity.ApplicationId == application.Id);
        var second = await context.Applications.AsNoTracking()
            .SingleAsync(entity => entity.ApplicationId == equivalent.Id);
        Assert.Equal(first.AllowedGrantTypesJson, second.AllowedGrantTypesJson);
        Assert.Equal(first.ScopesJson, second.ScopesJson);
    }

    private static async Task AssertCreateConflictAndCasAsync(
        EfApplicationStore applicationStore,
        EfCredentialStore credentialStore,
        ApplicationRecord application,
        CredentialRecord credential)
    {
        var newApplication = application with { Id = "app-create-conflict" };
        var createdApplication = await applicationStore.SaveWithRevisionAsync(newApplication, null);
        Assert.Equal(IamRevisionSaveStatus.Saved, createdApplication.Status);
        Assert.NotNull(createdApplication.Revision);
        var conflictingApplication = await applicationStore.SaveWithRevisionAsync(newApplication, null);
        Assert.Equal(IamRevisionSaveStatus.Conflict, conflictingApplication.Status);

        var newCredential = credential with { Id = "credential-create-conflict" };
        var createdCredential = await credentialStore.SaveWithRevisionAsync(newCredential, null);
        Assert.Equal(IamRevisionSaveStatus.Saved, createdCredential.Status);
        Assert.NotNull(createdCredential.Revision);
        var conflictingCredential = await credentialStore.SaveWithRevisionAsync(newCredential, null);
        Assert.Equal(IamRevisionSaveStatus.Conflict, conflictingCredential.Status);

        var applicationBefore = await applicationStore.FindWithRevisionAsync(application.TenantId, application.Id);
        Assert.NotNull(applicationBefore);
        var applicationCas = await applicationStore.SaveWithRevisionAsync(
            application with { DisplayName = "cas-application" },
            applicationBefore!.Revision);
        Assert.Equal(IamRevisionSaveStatus.Saved, applicationCas.Status);
        Assert.NotNull(applicationCas.Revision);
        var staleApplication = await applicationStore.SaveWithRevisionAsync(
            application with { DisplayName = "stale-application" },
            applicationBefore.Revision);
        Assert.Equal(IamRevisionSaveStatus.Conflict, staleApplication.Status);

        var credentialBefore = await credentialStore.FindWithRevisionAsync(credential.TenantId, credential.Id);
        Assert.NotNull(credentialBefore);
        var credentialCas = await credentialStore.SaveWithRevisionAsync(
            credential with { Status = CredentialStatus.Rotating },
            credentialBefore!.Revision);
        Assert.Equal(IamRevisionSaveStatus.Saved, credentialCas.Status);
        Assert.NotNull(credentialCas.Revision);
        var staleCredential = await credentialStore.SaveWithRevisionAsync(
            credential with { Status = CredentialStatus.Revoked },
            credentialBefore.Revision);
        Assert.Equal(IamRevisionSaveStatus.Conflict, staleCredential.Status);
    }

    private static async Task AssertConcurrentCasAsync(
        IdentityProviderFixture fixture,
        string provider,
        string tenant)
    {
        var application = Application(tenant, "app-concurrent-cas");
        var credential = Credential(
            tenant,
            "credential-concurrent-cas",
            new DateTimeOffset(2032, 5, 6, 7, 8, 9, TimeSpan.FromHours(5.5)).AddTicks(4321));

        await using (var seed = CreateContext(provider, fixture.ConnectionString))
        {
            var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
            Assert.Equal(
                IamRevisionSaveStatus.Saved,
                (await new EfApplicationStore(seed, access).SaveWithRevisionAsync(application, null)).Status);
            Assert.Equal(
                IamRevisionSaveStatus.Saved,
                (await new EfCredentialStore(seed, access).SaveWithRevisionAsync(credential, null)).Status);
        }

        await AssertConcurrentApplicationCasAsync(fixture.ConnectionString, provider, tenant, application);
        await AssertConcurrentCredentialCasAsync(fixture.ConnectionString, provider, tenant, credential);
    }

    private static async Task AssertConcurrentApplicationCasAsync(
        string connectionString,
        string provider,
        string tenant,
        ApplicationRecord application)
    {
        var barrier = new ConcurrentSaveBarrier(participants: 2);
        await using var firstContext = CreateContext(provider, connectionString, barrier);
        await using var secondContext = CreateContext(provider, connectionString, barrier);
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var firstStore = new EfApplicationStore(firstContext, access);
        var secondStore = new EfApplicationStore(secondContext, access);
        var firstRevision = Assert.IsType<string>((await firstStore.FindWithRevisionAsync(tenant, application.Id))!.Revision);
        var secondRevision = Assert.IsType<string>((await secondStore.FindWithRevisionAsync(tenant, application.Id))!.Revision);
        Assert.Equal(firstRevision, secondRevision);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var results = await Task.WhenAll(
            firstStore.SaveWithRevisionAsync(application with { DisplayName = "race-a" }, firstRevision, timeout.Token).AsTask(),
            secondStore.SaveWithRevisionAsync(application with { DisplayName = "race-b" }, secondRevision, timeout.Token).AsTask());

        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Saved);
        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Conflict);
    }

    private static async Task AssertConcurrentCredentialCasAsync(
        string connectionString,
        string provider,
        string tenant,
        CredentialRecord credential)
    {
        var barrier = new ConcurrentSaveBarrier(participants: 2);
        await using var firstContext = CreateContext(provider, connectionString, barrier);
        await using var secondContext = CreateContext(provider, connectionString, barrier);
        var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var firstStore = new EfCredentialStore(firstContext, access);
        var secondStore = new EfCredentialStore(secondContext, access);
        var firstRevision = Assert.IsType<string>((await firstStore.FindWithRevisionAsync(tenant, credential.Id))!.Revision);
        var secondRevision = Assert.IsType<string>((await secondStore.FindWithRevisionAsync(tenant, credential.Id))!.Revision);
        Assert.Equal(firstRevision, secondRevision);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var results = await Task.WhenAll(
            firstStore.SaveWithRevisionAsync(credential with { Status = CredentialStatus.Rotating }, firstRevision, timeout.Token).AsTask(),
            secondStore.SaveWithRevisionAsync(credential with { Status = CredentialStatus.Revoked }, secondRevision, timeout.Token).AsTask());

        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Saved);
        Assert.Single(results, result => result.Status == IamRevisionSaveStatus.Conflict);
    }

    private static async Task AssertMySqlTableEncodingAsync(IdentityIamDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            foreach (var tableName in new[]
                     {
                         IdentityIamEfModule.ApplicationTableName,
                         IdentityIamEfModule.CredentialTableName
                     })
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SHOW CREATE TABLE `{tableName}`";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var ddl = reader.GetString(1);
                Assert.Contains($"DEFAULT CHARSET={IdentityIamMySqlDbContext.CharacterSet}", ddl, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(IdentityIamMySqlDbContext.Collation, ddl, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static IdentityIamDbContext CreateContext(
        string provider,
        string connectionString,
        params IInterceptor[] interceptors) => provider switch
        {
            "PostgreSql" => new IdentityIamPostgreSqlDbContext(
                Configure(new DbContextOptionsBuilder<IdentityIamPostgreSqlDbContext>().UseNpgsql(connectionString), interceptors).Options),
            "SqlServer" => new IdentityIamSqlServerDbContext(
                Configure(new DbContextOptionsBuilder<IdentityIamSqlServerDbContext>().UseSqlServer(connectionString), interceptors).Options),
            "MySql" => new IdentityIamMySqlDbContext(
                Configure(new DbContextOptionsBuilder<IdentityIamMySqlDbContext>().UseMySQL(connectionString), interceptors).Options),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    private static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        IInterceptor[] interceptors)
        where TContext : DbContext
    {
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return builder;
    }

    private static string ExpectedProvider(string provider) => provider switch
    {
        "PostgreSql" => EfProviderNames.PostgreSql,
        "SqlServer" => EfProviderNames.SqlServer,
        "MySql" => EfProviderNames.MySql,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static ApplicationRecord Application(string tenant, string id) => new(
        id,
        tenant,
        "client-😀",
        "Display name 😀",
        ApplicationType.Confidential,
        ResourceOwnership.Foundation,
        new HashSet<string>(new[] { "client_credentials", "authorization_code", "grant-😀" }, StringComparer.Ordinal),
        new HashSet<string>(new[] { "scope:z", "scope:a", "scope-😀" }, StringComparer.Ordinal));

    private static CredentialRecord Credential(string tenant, string id, DateTimeOffset expiresAt) => new(
        id,
        tenant,
        CredentialSubjectType.Application,
        "app-primary",
        CredentialKind.ClientSecret,
        "hash-😀",
        "argon2id",
        CredentialStatus.Active,
        expiresAt);

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ConcurrentSaveBarrier(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int remaining = participants;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                release.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
