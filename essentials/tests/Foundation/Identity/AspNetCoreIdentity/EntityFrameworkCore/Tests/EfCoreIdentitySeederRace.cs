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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests;

/// <summary>The seeding step the two racing nodes are forced to collide on.</summary>
internal enum SeederRaceStep
{
    /// <summary>Both nodes read "administrator is not in the role" before either adds the membership.</summary>
    RoleMembership,

    /// <summary>
    /// Both nodes pass UserManager's create validation (the administrator does not exist yet); one then creates the user
    /// completely before the other reaches its lockout write, so the loser meets a row it has no revision for.
    /// </summary>
    AdminCreation
}

/// <summary>
/// Drives two <see cref="EfCoreIdentitySeeder"/> instances (two nodes) against one database and forces them to collide on
/// one seeding step through a <see cref="UserManager{TUser}"/> subclass that holds each node at a precise call. The hooks
/// record that the collision actually happened, and every iteration asserts it, so a run in which the nodes did not meet
/// (for example a barrier timeout) fails instead of passing vacuously. Compiled into the SQLite contract tests and the
/// PostgreSQL provider tests, which differ only in the persistence options and schema provisioning.
/// </summary>
internal static class EfCoreIdentitySeederRace
{
    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(10);

    public static async Task RunAsync(
        IdentityIamEntityFrameworkCoreOptions persistence,
        Func<IdentityIamDbContext, Task> ensureSchema,
        SeederRaceStep step,
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
            var meeting = new Meeting();
            var firstGate = new RaceGate(step, meeting);
            var secondGate = new RaceGate(step, meeting);
            await using var first = CreateNode(persistence, seed, tenantId, firstGate);
            await using var second = CreateNode(persistence, seed, tenantId, secondGate);

            await Task.WhenAll(
                Task.Run(() => first.Seeder.StartAsync(CancellationToken.None)),
                Task.Run(() => second.Seeder.StartAsync(CancellationToken.None)));

            AssertCollided(step, firstGate, secondGate);
            await first.WithScopeAsync(scope => AssertConvergedAsync(scope.ServiceProvider, tenantId, seed));
        }
    }

    private static void AssertCollided(SeederRaceStep step, RaceGate first, RaceGate second)
    {
        Assert.True(first.BarrierPassed && second.BarrierPassed, "Both nodes must have reached the forced collision point.");
        switch (step)
        {
            case SeederRaceStep.RoleMembership:
                Assert.True(first.AddToRoleCalls >= 1 && second.AddToRoleCalls >= 1, "Both nodes must have attempted the role-membership add.");
                Assert.True(first.LostMembershipRace || second.LostMembershipRace, "One node must have lost the add to the other (UserAlreadyInRole, ConcurrencyFailure or a revision conflict).");
                break;
            case SeederRaceStep.AdminCreation:
                Assert.NotEqual(first.IsCreationWinner, second.IsCreationWinner);
                Assert.True((first.IsCreationWinner ? second : first).LostCreateRace, "The losing node must have hit the lost-create-race store conflict.");
                break;
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
        RaceGate? gate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(persistence, seed, isDevelopmentOrDemo: true);
        services.Configure<AspNetCoreIdentityOptions>(options => options.DefaultTenantId = tenantId);
        if (gate is not null)
        {
            services.AddSingleton(gate);
            services.RemoveAll<UserManager<AspNetCoreIdentityUser>>();
            services.AddScoped<UserManager<AspNetCoreIdentityUser>, RacingUserManager>();
            services.AddScoped<IUserValidator<AspNetCoreIdentityUser>, RacingUserValidator>();
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

    /// <summary>State the two nodes of one iteration share.</summary>
    private sealed class Meeting
    {
        public Barrier Barrier { get; } = new(2);
        public TaskCompletionSource WinnerDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WinnerClaimed;
    }

    /// <summary>One node's hooks and what they observed. Pauses happen at most once per node, at the first matching call.</summary>
    private sealed class RaceGate(SeederRaceStep step, Meeting meeting)
    {
        private int membershipChecks;
        private int validations;

        public bool BarrierPassed { get; private set; }
        public int AddToRoleCalls;
        public bool LostMembershipRace { get; set; }
        public bool IsCreationWinner { get; private set; }
        public bool LostCreateRace { get; set; }

        /// <summary>Called after the node's first "is the administrator in the role?" read, with its answer.</summary>
        public async Task AfterMembershipCheckAsync(bool inRole)
        {
            // Only a read that said "not in the role" leads to the add; anything else leaves BarrierPassed false.
            if (step != SeederRaceStep.RoleMembership || Interlocked.Increment(ref membershipChecks) != 1 || inRole)
                return;

            BarrierPassed = await Task.Run(() => meeting.Barrier.SignalAndWait(RendezvousTimeout));
        }

        /// <summary>Called when the node's first user validation (UserManager.CreateAsync) has otherwise passed.</summary>
        public async Task AfterCreateValidationAsync()
        {
            if (step != SeederRaceStep.AdminCreation || Interlocked.Increment(ref validations) != 1)
                return;

            BarrierPassed = await Task.Run(() => meeting.Barrier.SignalAndWait(RendezvousTimeout));
            IsCreationWinner = Interlocked.Exchange(ref meeting.WinnerClaimed, 1) == 0;
            if (!IsCreationWinner)
                await meeting.WinnerDone.Task.WaitAsync(RendezvousTimeout);
        }

        public void AfterCreate()
        {
            if (IsCreationWinner)
                meeting.WinnerDone.TrySetResult();
        }
    }

    /// <summary>
    /// Runs last in UserManager's create validation, which is the final step before its lockout write: the hook point
    /// between "the administrator does not exist yet" and the store write that the race is about.
    /// </summary>
    private sealed class RacingUserValidator(RaceGate gate) : IUserValidator<AspNetCoreIdentityUser>
    {
        public async Task<IdentityResult> ValidateAsync(UserManager<AspNetCoreIdentityUser> manager, AspNetCoreIdentityUser user)
        {
            await gate.AfterCreateValidationAsync();
            return IdentityResult.Success;
        }
    }

    private sealed class RacingUserManager(IServiceProvider services, RaceGate gate) : ResolvingUserManager(services)
    {
        public override async Task<bool> IsInRoleAsync(AspNetCoreIdentityUser user, string role)
        {
            var inRole = await base.IsInRoleAsync(user, role);
            await gate.AfterMembershipCheckAsync(inRole);
            return inRole;
        }

        public override async Task<IdentityResult> AddToRoleAsync(AspNetCoreIdentityUser user, string role)
        {
            Interlocked.Increment(ref gate.AddToRoleCalls);
            try
            {
                var result = await base.AddToRoleAsync(user, role);
                if (result.Errors.Any(error => error.Code is nameof(IdentityErrorDescriber.UserAlreadyInRole) or nameof(IdentityErrorDescriber.ConcurrencyFailure)))
                    gate.LostMembershipRace = true;
                return result;
            }
            catch (IdentityRevisionConflictException)
            {
                gate.LostMembershipRace = true;
                throw;
            }
        }

        public override async Task<IdentityResult> CreateAsync(AspNetCoreIdentityUser user)
        {
            try
            {
                return await base.CreateAsync(user);
            }
            catch (IdentityRevisionConflictException)
            {
                gate.LostCreateRace = true;
                throw;
            }
            finally
            {
                gate.AfterCreate();
            }
        }
    }
}

/// <summary>A <see cref="UserManager{TUser}"/> test double base that takes its collaborators from the scope, so a subclass overrides one member without restating UserManager's constructor.</summary>
internal abstract class ResolvingUserManager(IServiceProvider services) : UserManager<AspNetCoreIdentityUser>(
    services.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>(),
    services.GetRequiredService<IOptions<IdentityOptions>>(),
    services.GetRequiredService<IPasswordHasher<AspNetCoreIdentityUser>>(),
    services.GetServices<IUserValidator<AspNetCoreIdentityUser>>(),
    services.GetServices<IPasswordValidator<AspNetCoreIdentityUser>>(),
    services.GetRequiredService<ILookupNormalizer>(),
    services.GetRequiredService<IdentityErrorDescriber>(),
    services,
    services.GetRequiredService<ILogger<UserManager<AspNetCoreIdentityUser>>>());
