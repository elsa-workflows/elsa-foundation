using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Every Identity IAM row is stamped when written, and a row a newer module version wrote reports skew, as itself,
/// before any store, coordinator or receipt reconciliation trusts a column of it. Where a row has a JSON column its
/// content is damaged too, so a check that ran after deserialization, or after an integrity clause, would surface a
/// different failure.
/// </summary>
public sealed class IdentitySchemaVersionSkewTests : IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private const string UserId = "user-a";
    private const string RoleId = "role-a";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-identity-skew-{Guid.NewGuid():N}.db");
    private ServiceProvider provider = null!;
    private AsyncServiceScope scope;

    private IdentityIamDbContext Context => Get<IdentityIamDbContext>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddPersistenceCore(Tenant);
        services.AddIdentityIamEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={databasePath};Default Timeout=5"
        });
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        scope = provider.CreateAsyncScope();
        await Context.Database.EnsureCreatedAsync();

        // Unique email is what writes an email reservation, so every IAM table holds at least one row.
        await new EfUserStore(Context, Get<IPersistenceAccessContextAccessor>(), emailUniquenessPolicy: IdentityEmailUniquenessPolicy.Unique).SaveAsync(new UserRecord(UserId, Tenant, "Ada", "ada@example.test", "Ada", UserStatus.Active, ResourceOwnership.Foundation, Set(), Set("identity.users.read")));
        await Get<IRoleStore>().SaveAsync(new RoleRecord(RoleId, Tenant, "Operators", "Operators", Set("identity.users.read"), System: false));
        await Get<IClaimMappingStore>().SaveAsync(new ClaimMappingRule("rule-a", Tenant, "google", "groups", "admins", Set(RoleId), Set(), 10, StopOnMatch: true));
        await Get<IApplicationStore>().SaveAsync(new ApplicationRecord("app-a", Tenant, "client-a", "App", ApplicationType.Confidential, ResourceOwnership.Foundation, Set("client_credentials"), Set("openid")));
        await Get<ICredentialStore>().SaveAsync(new CredentialRecord("credential-a", Tenant, CredentialSubjectType.Application, "app-a", CredentialKind.ClientSecret, "hash", "Argon2id", CredentialStatus.Active, null));
        await Get<IExternalIdentityStore>().SaveAsync(new ExternalIdentityRecord(Tenant, "google", "subject-a", UserId, DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Auto));
        await Get<ITenantMembershipStore>().SaveAsync(new TenantMembershipRecord(Tenant, UserId, TenantMembershipStatus.Active, Set(RoleId), Set()));
        var relationships = Get<EfIdentityAuthorityRelationshipCoordinator>();
        Assert.True((await relationships.AddUserClaimsAsync(Tenant, UserId, await UserRevisionAsync(), [new UserClaimEntity { ClaimType = "type", ClaimValue = "value" }])).Succeeded);
        Assert.True((await relationships.SaveUserTokenAsync(Tenant, UserId, await UserRevisionAsync(), new UserTokenEntity { LoginProvider = "Identity", Name = "token", Value = "value" })).Succeeded);
        Assert.True((await relationships.AddUserRoleAsync(Tenant, UserId, RoleId, await UserRevisionAsync(), new UserRoleEntity())).Succeeded);
        Assert.True((await relationships.SaveRoleClaimAsync(Tenant, RoleId, await RoleRevisionAsync(), new RoleClaimEntity { ClaimType = "type", ClaimValue = "value" })).Succeeded);
        Context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        TemporarySqliteDatabase.ClearPoolAndDeleteFiles(databasePath);
    }

    [Fact]
    public async Task Every_row_is_stamped_with_the_family_version_when_written()
    {
        // The finalization tables belong to their own family, and their store stamps them.
        foreach (var entityType in Context.Model.GetEntityTypes().ExcludingSchemaFinalization())
        {
            var rows = await ((IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                    .MakeGenericMethod(entityType.ClrType).Invoke(Context, null)!)
                .AsNoTracking()
                .ToListAsync();
            Assert.True(rows.Count > 0, $"The fixture wrote no {entityType.GetTableName()} row to check.");
            Assert.All(rows, row => Assert.Equal(IdentityIamEfModule.SchemaVersion, Context.Entry(row).Property("SchemaVersion").CurrentValue));
        }
    }

    [Theory]
    [MemberData(nameof(SkewedReads))]
    public async Task A_row_with_a_newer_incompatible_schema_reports_skew_on_every_read_and_write_that_trusts_it(string table)
    {
        var (arrange, operations) = Scenario(table);
        await arrange();

        foreach (var operation in operations)
        {
            EfSchemaVersionSkewTestSupport.AssertSchemaVersionSkew(
                await Assert.ThrowsAsync<EfSchemaVersionSkewException>(operation), "IdentityIam", IdentityIamEfModule.SchemaVersion);
            Context.ChangeTracker.Clear();
        }
    }

    public static TheoryData<string> SkewedReads() =>
    [
        "application", "credential", "user", "role", "claim mapping", "external identity", "tenant membership",
        "user claim", "user token", "user role", "role claim", "user-name reservation", "email reservation",
        "role-name reservation", "mutation receipt"
    ];

    private (Func<Task> Arrange, Func<Task>[] Operations) Scenario(string table)
    {
        var users = Get<IUserStore>();
        var roles = Get<IRoleStore>();
        var aggregates = Get<EfIdentityAuthorityAggregateCoordinator>();
        Func<Task> deleteUser = async () => await aggregates.DeleteUserAsync(Tenant, UserId, await UserRevisionAsync());
        Func<Task> deleteRole = async () => await aggregates.DeleteRoleAsync(Tenant, RoleId, await RoleRevisionAsync());
        return table switch
        {
            "application" => (
                () => SkewAsync<ApplicationEntity>(row => row.AllowedGrantTypesJson = EfSchemaVersionSkewTestSupport.CorruptContentJson),
                [
                    () => Get<IApplicationStore>().FindAsync(Tenant, "app-a").AsTask(),
                    () => Get<IRevisionAwareApplicationStore>().FindWithRevisionAsync(Tenant, "app-a").AsTask(),
                    () => Get<IApplicationStore>().SaveAsync(new ApplicationRecord("app-a", Tenant, "client-b", "App", ApplicationType.Public, ResourceOwnership.Foundation, Set(), Set())).AsTask()
                ]),
            "credential" => (
                // A damaged lookup key made this read quietly return nothing; skew is now reported first.
                () => SkewAsync<CredentialEntity>(row => row.CredentialLookupKey = "corrupt"),
                [
                    () => Get<ICredentialStore>().FindAsync(Tenant, "credential-a").AsTask(),
                    () => Get<ICredentialStore>().SaveAsync(new CredentialRecord("credential-a", Tenant, CredentialSubjectType.Application, "app-a", CredentialKind.ClientSecret, "hash-b", "Argon2id", CredentialStatus.Active, null)).AsTask()
                ]),
            "user" => (
                () => SkewAsync<UserEntity>(row => row.RoleIdsJson = EfSchemaVersionSkewTestSupport.CorruptContentJson),
                [
                    () => users.FindAsync(Tenant, UserId).AsTask(),
                    () => users.FindByEmailAsync(Tenant, "ada@example.test").AsTask(),
                    () => users.SaveAsync(new UserRecord(UserId, Tenant, "Ada", "ada@example.test", "Ada B", UserStatus.Active, ResourceOwnership.Foundation, Set(), Set())).AsTask(),
                    deleteUser
                ]),
            "role" => (
                () => SkewAsync<RoleEntity>(row => row.PermissionsJson = EfSchemaVersionSkewTestSupport.CorruptContentJson),
                [
                    () => roles.FindAsync(Tenant, RoleId).AsTask(),
                    () => roles.ListAsync(Tenant).AsTask(),
                    () => Get<IPagedRoleStore>().ListPageAsync(Tenant, new IamPageRequest(skip: 0, take: 10)).AsTask(),
                    () => roles.SaveAsync(new RoleRecord(RoleId, Tenant, "Operators", "Changed", Set(), System: false)).AsTask(),
                    deleteRole
                ]),
            "claim mapping" => (
                () => SkewAsync<ClaimMappingEntity>(row => row.GrantRolesJson = EfSchemaVersionSkewTestSupport.CorruptContentJson),
                [
                    () => Get<IClaimMappingStore>().ListForProviderAsync(Tenant, "google").AsTask(),
                    () => Get<IRevisionAwareClaimMappingStore>().FindWithRevisionAsync(Tenant, "google", "rule-a").AsTask(),
                    () => Get<IClaimMappingStore>().SaveAsync(new ClaimMappingRule("rule-a", Tenant, "google", "groups", "admins", Set(), Set(), 20, StopOnMatch: false)).AsTask()
                ]),
            "external identity" => (
                () => SkewAsync<ExternalIdentityEntity>(_ => { }),
                [
                    () => Get<IExternalIdentityStore>().FindBySubjectAsync(Tenant, "google", "subject-a").AsTask(),
                    () => Get<IExternalIdentityStore>().ListForUserAsync(Tenant, UserId).AsTask(),
                    deleteUser
                ]),
            "tenant membership" => (
                () => SkewAsync<TenantMembershipEntity>(row => row.RoleIdsJson = EfSchemaVersionSkewTestSupport.CorruptContentJson),
                [
                    () => Get<ITenantMembershipStore>().FindAsync(Tenant, UserId).AsTask(),
                    () => Get<ITenantMembershipStore>().SaveAsync(new TenantMembershipRecord(Tenant, UserId, TenantMembershipStatus.Suspended, Set(), Set())).AsTask(),
                    deleteUser
                ]),
            "user claim" => (() => SkewAsync<UserClaimEntity>(_ => { }), [deleteUser]),
            "user token" => (() => SkewAsync<UserTokenEntity>(_ => { }), [deleteUser]),
            "user role" => (() => SkewAsync<UserRoleEntity>(_ => { }), [deleteUser, deleteRole]),
            "role claim" => (() => SkewAsync<RoleClaimEntity>(_ => { }), [deleteRole]),
            "user-name reservation" => (() => SkewAsync<UserNameReservationEntity>(_ => { }), [deleteUser]),
            "email reservation" => (() => SkewAsync<EmailReservationEntity>(_ => { }), [deleteUser]),
            "role-name reservation" => (() => SkewAsync<RoleNameReservationEntity>(_ => { }), [deleteRole]),
            _ => (
                // An expired receipt is reclaimed before the next mutation; a skewed one is refused, never deleted.
                () => SkewAsync<MutationReceiptEntity>(row => row.ExpiresAt = DateTimeOffset.UnixEpoch),
                [() => Get<EfIdentityAtomicWrite>().CleanupExpiredAsync()])
        };
    }

    private async Task SkewAsync<TEntity>(Action<TEntity> damage) where TEntity : class
    {
        var rows = await Context.Set<TEntity>().ToListAsync();
        Assert.NotEmpty(rows);
        await EfSchemaVersionSkewTestSupport.ArrangeSkewedRowAsync(
            Context,
            version => rows.ForEach(row => Context.Entry(row).Property("SchemaVersion").CurrentValue = version),
            _ => rows.ForEach(damage));
    }

    private async Task<long> UserRevisionAsync() => (await Context.Users.AsNoTracking().SingleAsync()).Revision;

    private async Task<long> RoleRevisionAsync() => (await Context.Roles.AsNoTracking().SingleAsync()).Revision;

    private T Get<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
