using System.Data.Common;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests;

/// <summary>
/// Drives two <see cref="EfCoreIdentitySeeder"/> instances (two nodes) against one database and forces them to
/// interleave in the role-membership tail: each node is held after its "is the administrator in the role?" read until
/// the other has made the same read, so both then try to add the membership. Compiled into the SQLite contract tests
/// and the PostgreSQL provider tests, which differ only in the persistence options and schema provisioning.
/// </summary>
internal static class EfCoreIdentitySeederRace
{
    public static async Task RunAsync(
        IdentityIamEntityFrameworkCoreOptions persistence,
        Func<IdentityIamDbContext, Task> ensureSchema,
        int iterations)
    {
        var seed = new IdentitySeedOptions
        {
            UserName = "admin",
            Password = "Correct Horse1!",
            Email = "admin@example.test",
            RoleName = "Administrators"
        };
        var run = Guid.NewGuid().ToString("N");

        await using (var schemaNode = CreateNode(persistence, seed, $"{run}-schema", gate: null))
            await schemaNode.WithScopeAsync(scope => ensureSchema(scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>()));

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            // A fresh tenant per iteration is a fresh first boot: nothing is seeded yet.
            var tenantId = $"seeder-race-{run}-{iteration}";
            var barrier = new Barrier(2);
            await using var first = CreateNode(persistence, seed, tenantId, new MembershipCheckGate(barrier));
            await using var second = CreateNode(persistence, seed, tenantId, new MembershipCheckGate(barrier));

            await Task.WhenAll(
                Task.Run(() => first.Seeder.StartAsync(CancellationToken.None)),
                Task.Run(() => second.Seeder.StartAsync(CancellationToken.None)));

            await first.WithScopeAsync(scope => AssertConvergedAsync(scope.ServiceProvider, tenantId, seed));
        }
    }

    private static async Task AssertConvergedAsync(IServiceProvider services, string tenantId, IdentitySeedOptions seed)
    {
        var users = services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var user = await users.FindByNameAsync(seed.UserName);
        Assert.NotNull(user);
        Assert.True(await users.IsInRoleAsync(user!, seed.RoleName));

        var db = services.GetRequiredService<IdentityIamDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(1, await db.Roles.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(1, await db.UserRoles.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(1, await db.TenantMemberships.CountAsync(x => x.TenantId == tenantId));
    }

    private static Node CreateNode(
        IdentityIamEntityFrameworkCoreOptions persistence,
        IdentitySeedOptions seed,
        string tenantId,
        MembershipCheckGate? gate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(persistence, seed, isDevelopmentOrDemo: true);
        services.Configure<AspNetCoreIdentityOptions>(options => options.DefaultTenantId = tenantId);
        if (gate is not null)
        {
            services.AddSingleton(gate);
            services.AddSingleton(typeof(IDbContextOptionsConfiguration<>), typeof(GateConfiguration<>));
        }
        return new Node(services.BuildServiceProvider(), tenantId);
    }

    private sealed class Node(ServiceProvider provider, string tenantId) : IAsyncDisposable
    {
        public EfCoreIdentitySeeder Seeder => provider.GetRequiredService<EfCoreIdentitySeeder>();

        public async Task WithScopeAsync(Func<AsyncServiceScope, Task> action)
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
            await action(scope);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class GateConfiguration<TContext>(MembershipCheckGate gate) : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.AddInterceptors(gate);
    }

    /// <summary>
    /// Holds the first read of the user-role table on this node until the peer node has made its own, once.
    /// The timeout only keeps a node that never reaches the read from hanging the test.
    /// </summary>
    private sealed class MembershipCheckGate(Barrier barrier) : DbCommandInterceptor
    {
        private int armed = 1;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(IdentityIamEfModule.UserRoleTableName, StringComparison.Ordinal)
                && Interlocked.Exchange(ref armed, 0) == 1)
                await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)), cancellationToken);
            return result;
        }
    }
}
