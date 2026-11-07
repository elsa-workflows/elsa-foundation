using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ExternalUserAdmissionTests
{
    private static readonly ExternalIdentityKey Key = new("tenant", "company", "https://issuer/", "subject");

    [Fact]
    public async Task In_memory_admission_is_atomic_preserves_existing_roles_and_refuses_stale_or_disabled_users()
    {
        var store = new InMemoryIdentityStore();
        var candidate = Candidate("new");
        Assert.True((await store.AdmitAsync(new(Key, candidate, true, ExternalIdentityLinkPolicy.Auto))).Admitted);
        var repeated = await store.AdmitAsync(new(Key, Candidate("loser"), true, ExternalIdentityLinkPolicy.Auto));
        Assert.True(repeated.Admitted);
        Assert.Equal(candidate.Id, repeated.UserId);
        Assert.Null(await ((IUserStore)store).FindAsync("tenant", "loser"));
        var changed = candidate with { RoleIds = new HashSet<string> { "manually-assigned" } };
        await store.SaveAsync(changed);
        var anotherKey = new ExternalIdentityKey("tenant", "another", Key.Issuer, Key.Subject);
        Assert.Equal("admission-conflict", (await store.AdmitAsync(new(anotherKey, candidate, false, ExternalIdentityLinkPolicy.Auto))).Failure);
        Assert.True((await store.AdmitAsync(new(anotherKey, changed, false, ExternalIdentityLinkPolicy.Auto))).Admitted);
        Assert.Equal(changed.RoleIds, (await ((IUserStore)store).FindAsync("tenant", "new"))!.RoleIds);
        await store.SetStatusAsync("tenant", candidate.Id, UserStatus.Disabled);
        Assert.Equal("user-unavailable", (await store.AdmitAsync(new(Key, candidate, false, ExternalIdentityLinkPolicy.Auto))).Failure);
    }

    [Fact]
    public async Task Sqlite_concurrent_first_signins_commit_one_user_and_link_and_return_the_same_canonical_owner()
    {
        await using var database = new ExternalIdentityLinkTests.LinkDatabase();
        await using (var seed = await database.Open())
            await new EfRoleStore(seed, database.Access).SaveAsync(new("reader", "tenant", "Reader", null, new HashSet<string> { "workflows:read" }, false));
        var barrier = new TwoStarts();
        await using var first = await database.Open();
        await using var second = await database.Open();
        var a = Candidate("first") with { RoleIds = new HashSet<string> { "reader" } };
        var b = Candidate("second") with { RoleIds = new HashSet<string> { "reader" } };
        var results = await Task.WhenAll(
            Task.Run(async () => { await barrier.Arrive(default); return await new EfExternalIdentityStore(first, database.Access).AdmitAsync(new(Key, a, true, ExternalIdentityLinkPolicy.Auto)); }),
            Task.Run(async () => { await barrier.Arrive(default); return await new EfExternalIdentityStore(second, database.Access).AdmitAsync(new(Key, b, true, ExternalIdentityLinkPolicy.Auto)); }));
        Assert.All(results, result => Assert.True(result.Admitted, result.Failure));
        Assert.Equal(results[0].UserId, results[1].UserId);
        await using var inspection = await database.Open();
        var owner = Assert.Single(await inspection.Users.AsNoTracking().ToListAsync());
        Assert.Null(owner.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(owner.SecurityStamp));
        Assert.Equal(results[0].UserId, owner.UserId);
        Assert.Equal(owner.UserId, (await inspection.ExternalIdentities.AsNoTracking().SingleAsync()).UserId);
        Assert.Single(await inspection.UserNameReservations.ToListAsync());
        var stored = (await new EfUserStore(inspection, database.Access).FindAsync("tenant", owner.UserId))!;
        Assert.Equal(new HashSet<string> { "reader" }, stored.RoleIds);
    }

    [Fact]
    public async Task Failed_link_write_rolls_back_the_new_user_its_name_reservation_and_inner_receipts()
    {
        await using var database = new ExternalIdentityLinkTests.LinkDatabase();
        await using var db = await database.Open(new FailLoginInsert());
        var store = new EfExternalIdentityStore(db, database.Access);
        await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
            store.AdmitAsync(new(Key, Candidate("orphan"), true, ExternalIdentityLinkPolicy.Auto)).AsTask());
        Assert.Empty(await db.Users.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ExternalIdentities.AsNoTracking().ToListAsync());
        Assert.Empty(await db.UserNameReservations.AsNoTracking().ToListAsync());
        Assert.Empty(await db.MutationReceipts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Existing_user_matching_links_only_the_unchanged_active_snapshot_and_never_reassigns_saved_roles()
    {
        await using var database = new ExternalIdentityLinkTests.LinkDatabase();
        await using var db = await database.Open();
        var users = new EfUserStore(db, database.Access);
        var original = Candidate("existing");
        await users.SaveAsync(original);
        var changed = original with { RoleIds = new HashSet<string> { "manual" } };
        await users.SaveAsync(changed);
        var store = new EfExternalIdentityStore(db, database.Access);
        Assert.Equal("admission-conflict", (await store.AdmitAsync(new(Key, original, false, ExternalIdentityLinkPolicy.Auto))).Failure);
        Assert.Empty(await db.ExternalIdentities.ToListAsync());
        Assert.True((await store.AdmitAsync(new(Key, changed, false, ExternalIdentityLinkPolicy.Auto))).Admitted);
        Assert.Equal(changed.RoleIds, (await users.FindAsync("tenant", changed.Id))!.RoleIds);
        await users.SetStatusAsync("tenant", changed.Id, UserStatus.Disabled);
        Assert.Equal("user-unavailable", (await store.AdmitAsync(new(Key, changed, false, ExternalIdentityLinkPolicy.Auto))).Failure);
    }

    [Fact]
    public async Task Sqlite_matching_rejects_a_membership_changed_after_the_user_was_assessed()
    {
        await using var database = new ExternalIdentityLinkTests.LinkDatabase();
        await using var db = await database.Open();
        var user = Candidate("existing");
        await new EfUserStore(db, database.Access).SaveAsync(user);
        var membership = new TenantMembershipRecord("tenant", user.Id, TenantMembershipStatus.Active,
            new HashSet<string> { "manual" }, new HashSet<string>());
        await new EfTenantMembershipStore(db, database.Access).SaveAsync(membership);
        var store = new EfExternalIdentityStore(db, database.Access);
        var stale = await store.AdmitAsync(new(Key, user, false, ExternalIdentityLinkPolicy.Auto));
        Assert.False(stale.Admitted);
        Assert.Equal("admission-conflict", stale.Failure);
        Assert.Empty(await db.ExternalIdentities.ToListAsync());
        var fresh = await store.AdmitAsync(new(Key, user, false, ExternalIdentityLinkPolicy.Auto) { ExpectedMembership = membership });
        Assert.True(fresh.Admitted);
        Assert.Equal(membership.RoleIds, (await new EfTenantMembershipStore(db, database.Access).FindAsync("tenant", user.Id))!.RoleIds);
    }

    [Fact]
    public async Task Missing_default_role_or_existing_user_identifier_refuses_creation_without_partial_writes()
    {
        await using var database = new ExternalIdentityLinkTests.LinkDatabase();
        await using var db = await database.Open();
        var store = new EfExternalIdentityStore(db, database.Access);
        var candidate = Candidate("new") with { RoleIds = new HashSet<string> { "deleted-role" } };
        Assert.Equal("role-unavailable", (await store.AdmitAsync(new(Key, candidate, true, ExternalIdentityLinkPolicy.Auto))).Failure);
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.ExternalIdentities.ToListAsync());
        await database.Seed(db, candidate.Id);
        var request = new ExternalUserAdmission(Key, Candidate(candidate.Id), true, ExternalIdentityLinkPolicy.Auto);
        Assert.Equal("admission-conflict", (await store.AdmitAsync(request)).Failure);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Empty(await db.ExternalIdentities.ToListAsync());
    }

    private static UserRecord Candidate(string id) => new(id, "tenant", id, null, id, UserStatus.Active, ResourceOwnership.External,
        new HashSet<string>(), new HashSet<string>());

    private sealed class FailLoginInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ExternalIdentityEntity>().Any(entry => entry.State == EntityState.Added))
                throw new IOException("Injected link persistence failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TwoStarts
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;
        public async Task Arrive(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref reads) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }

}
