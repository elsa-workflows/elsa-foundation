using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Executes the shared, timing-free IAM scenario against each real SQLite-backed store implementation.
/// Native-plan evidence remains a separate prerequisite for performance measurement and verdicts.
/// </summary>
public sealed class IamNormalizedLookupSqliteCorrectnessTests
{
    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Groundwork_store_contracts_produce_the_ratified_digest()
    {
        await using var driver = new SqliteGroundworkProviderDriver();
        await driver.InitializeAsync(CancellationToken.None);
        await driver.ResetPhysicalAsync([new Elsa.Foundation.Identity.Persistence.Groundwork.IdentityGroundworkStorageManifestSource()], CancellationToken.None);
        await using var client = await driver.OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope(IamNormalizedLookupWorkload.TenantId)),
            CancellationToken.None);
        var boundedStore = client.BoundedDocumentStore
                           ?? throw new InvalidOperationException("The SQLite Groundwork provider did not expose bounded document storage.");
        var access = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope(IamNormalizedLookupWorkload.TenantId)));
        var adapter = new GroundworkIdentityWorkloadAdapter(
            new GroundworkIdentityUserStore(client.DocumentStore, access, boundedStore),
            new GroundworkIdentityRoleStore(client.DocumentStore, access, boundedStore));

        AssertRatified(await new IamNormalizedLookupWorkload().ExecuteAsync(adapter));
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Ef_identity_store_contracts_produce_the_ratified_digest()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationIdentityDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<AspNetCoreIdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationIdentityDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var db = serviceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
        var adapter = new EfIdentityWorkloadAdapter(serviceProvider.GetRequiredService<IServiceScopeFactory>());

        AssertRatified(await new IamNormalizedLookupWorkload().ExecuteAsync(adapter));
    }

    private static void AssertRatified(IamNormalizedLookupResult result)
    {
        Assert.Equal(IamNormalizedLookupWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(IamNormalizedLookupWorkload.ExpectedResultDigest, result.ResultDigest);
    }

    private sealed class EfIdentityWorkloadAdapter(IServiceScopeFactory scopeFactory) : IIamIdentityWorkloadAdapter
    {
        public Task<IdentityResult> CreateUserAsync(AspNetCoreIdentityUser user, CancellationToken token) =>
            UseUserStoreAsync(store => store.CreateAsync(user, token));

        public Task<IdentityResult> CreateRoleAsync(IdentityRole role, CancellationToken token) =>
            UseRoleStoreAsync(store => store.CreateAsync(role, token));

        public async Task AddToRoleAsync(AspNetCoreIdentityUser user, string role, CancellationToken token)
        {
            using var scope = scopeFactory.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>();
            var memberships = users as IUserRoleStore<AspNetCoreIdentityUser>
                ?? throw new InvalidOperationException("The EF Identity user store must support user-role membership.");
            await memberships.AddToRoleAsync(user, role, token);
            IamNormalizedLookupWorkload.AssertSucceeded(await users.UpdateAsync(user, token), "persist-user-role-link");
        }

        public Task<AspNetCoreIdentityUser?> FindUserByNormalizedNameAsync(string value, CancellationToken token) =>
            UseUserStoreAsync(store => store.FindByNameAsync(value, token));

        public Task<AspNetCoreIdentityUser?> FindUserByNormalizedEmailAsync(string value, CancellationToken token) =>
            UseEmailStoreAsync(store => store.FindByEmailAsync(value, token));

        public Task<IdentityRole?> FindRoleByNormalizedNameAsync(string value, CancellationToken token) =>
            UseRoleStoreAsync(store => store.FindByNameAsync(value, token));

        public Task<IList<string>> GetRolesAsync(AspNetCoreIdentityUser user, CancellationToken token) =>
            UseRoleMembershipStoreAsync(store => store.GetRolesAsync(user, token));

        public Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(string value, CancellationToken token) =>
            UseRoleMembershipStoreAsync(store => store.GetUsersInRoleAsync(value, token));

        public Task<AspNetCoreIdentityUser?> FindUserByIdAsync(string value, CancellationToken token) =>
            UseUserStoreAsync(store => store.FindByIdAsync(value, token));

        public Task<IdentityResult> UpdateUserAsync(AspNetCoreIdentityUser user, CancellationToken token) =>
            UseUserStoreAsync(store => store.UpdateAsync(user, token));

        private async Task<T> UseUserStoreAsync<T>(Func<IUserStore<AspNetCoreIdentityUser>, Task<T>> operation)
        {
            using var scope = scopeFactory.CreateScope();
            return await operation(scope.ServiceProvider.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>());
        }

        private async Task<T> UseRoleStoreAsync<T>(Func<IRoleStore<IdentityRole>, Task<T>> operation)
        {
            using var scope = scopeFactory.CreateScope();
            return await operation(scope.ServiceProvider.GetRequiredService<IRoleStore<IdentityRole>>());
        }

        private async Task<T> UseEmailStoreAsync<T>(Func<IUserEmailStore<AspNetCoreIdentityUser>, Task<T>> operation)
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>() as IUserEmailStore<AspNetCoreIdentityUser>
                ?? throw new InvalidOperationException("The EF Identity user store must support normalized email lookup.");
            return await operation(store);
        }

        private async Task<T> UseRoleMembershipStoreAsync<T>(Func<IUserRoleStore<AspNetCoreIdentityUser>, Task<T>> operation)
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>() as IUserRoleStore<AspNetCoreIdentityUser>
                ?? throw new InvalidOperationException("The EF Identity user store must support user-role membership.");
            return await operation(store);
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
