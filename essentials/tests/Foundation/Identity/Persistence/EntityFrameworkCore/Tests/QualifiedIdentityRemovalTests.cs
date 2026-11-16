using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed partial class IdentityAuthorityEntityFrameworkCoreBehaviorTests
{
    [Fact]
    public async Task Qualified_removal_save_failure_preserves_the_link_registry_counter_and_account_epoch()
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(path, "tenant-a");
            await scope.Users.SaveAsync(User("tenant-a", "owner", "Owner", "owner@example.test"));
            var record = ExternalIdentity("tenant-a", "owner", "company", "subject") with { Issuer = "https://issuer.example" };
            await scope.ExternalIdentities.SaveAsync(record);
            var key = new ExternalIdentityKey("tenant-a", "company", record.Issuer, "subject");
            var link = (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(key))!;
            var guard = (await scope.RevisionUsers.FindRevisionGuardAsync("tenant-a", "owner"))!;
            var stamp = await scope.Context.Users.AsNoTracking().Select(row => row.SecurityStamp).SingleAsync();
            await using var faulted = new IdentityIamSqliteDbContext(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
                .UseSqlite(ConnectionTo(path)).AddInterceptors(new QualifiedRemovalSaveFault()).Options);
            var store = new EfExternalIdentityStore(faulted, scope.Access);
            await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => store.RemoveWithRevisionAsync(key, guard, link.Revision).AsTask());
            Assert.Equal(link.Revision, (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(key))!.Revision);
            Assert.Equal(guard.Revision, (await scope.RevisionUsers.FindRevisionGuardAsync("tenant-a", "owner"))!.Revision);
            Assert.Equal(stamp, await scope.Context.Users.AsNoTracking().Select(row => row.SecurityStamp).SingleAsync());
            Assert.Single(await scope.ExternalIdentities.ListForUserAsync("tenant-a", "owner"));
        }
        finally { DeleteDatabaseFiles(path); }
    }

    private sealed class QualifiedRemovalSaveFault : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("qualified-removal-save-private-canary");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualified_removal_preserves_other_links_and_epochs_and_never_reuses_a_removed_revision(bool historicalCounter)
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(path, "tenant-a");
            await scope.Users.SaveAsync(User("tenant-a", "owner", "Owner", "owner@example.test"));
            var first = ExternalIdentity("tenant-a", "owner", "company", "subject") with { Issuer = "https://issuer.example" };
            var other = ExternalIdentity("tenant-a", "owner", "partner", "subject") with { Issuer = "https://partner.example" };
            await scope.ExternalIdentities.SaveAsync(first); await scope.ExternalIdentities.SaveAsync(other);
            if (historicalCounter)
                await scope.Context.ExternalIdentities.Where(row => row.Provider == "company").ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 200L));
            var key = new ExternalIdentityKey("tenant-a", "company", first.Issuer, "subject");
            var previous = (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(key))!;
            var guard = (await scope.RevisionUsers.FindRevisionGuardAsync("tenant-a", "owner"))!;
            var stamp = await scope.Context.Users.AsNoTracking().Select(row => row.SecurityStamp).SingleAsync();
            var sibling = (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(new("tenant-a", "partner", other.Issuer, "subject")))!;
            Assert.True(scope.RevisionExternalIdentities.SupportsQualifiedRemoval);
            var removed = await scope.RevisionExternalIdentities.RemoveWithRevisionAsync(key, guard, previous.Revision);
            Assert.Equal(ExternalIdentityRemovalStatus.Removed, removed.Status);
            Assert.Null(await scope.ExternalIdentities.FindByKeyAsync(key));
            Assert.Equal(stamp, await scope.Context.Users.AsNoTracking().Select(row => row.SecurityStamp).SingleAsync());
            Assert.Equal(sibling.Revision, (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(new("tenant-a", "partner", other.Issuer, "subject")))!.Revision);
            Assert.Single(await scope.ExternalIdentities.ListForUserAsync("tenant-a", "owner"));
            // Deliberately reuse the exact timestamps and identity tuple: time cannot identify a link instance.
            await scope.ExternalIdentities.SaveAsync(first);
            var recreated = (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(key))!;
            Assert.True(string.CompareOrdinal(previous.Revision, recreated.Revision) < 0);
            Assert.Equal(first.LinkedAt, recreated.Record.LinkedAt);
            Assert.Equal(ExternalIdentityRemovalStatus.Removed, (await scope.RevisionExternalIdentities.RemoveWithRevisionAsync(key, guard, previous.Revision)).Status);
            Assert.Equal(recreated.Revision, (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(key))!.Revision);
        }
        finally { DeleteDatabaseFiles(path); }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("user-revision")]
    [InlineData("link-revision")]
    [InlineData("issuer")]
    public async Task Qualified_removal_refuses_stale_or_wrong_identity_boundaries_without_changing_any_owner(string invalid)
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var scope = await EfIdentityScope.OpenAsync(path, "tenant-a");
            await scope.Users.SaveAsync(User("tenant-a", "owner", "Owner", "owner@example.test"));
            await scope.Users.SaveAsync(User("tenant-a", "other", "Other", "other@example.test"));
            var link = ExternalIdentity("tenant-a", "owner", "company", "subject") with { Issuer = "https://issuer.example" };
            await scope.ExternalIdentities.SaveAsync(link);
            var key = new ExternalIdentityKey("tenant-a", "company", link.Issuer, "subject");
            var saved = (await scope.RevisionExternalIdentities.FindByKeyWithRevisionAsync(key))!;
            var guard = (await scope.RevisionUsers.FindRevisionGuardAsync("tenant-a", invalid == "owner" ? "other" : "owner"))!;
            if (invalid == "user-revision")
            {
                var user = (await scope.Users.FindAsync("tenant-a", "owner"))!;
                await scope.Users.SaveAsync(user with { DisplayName = "changed" });
            }
            if (invalid == "link-revision") await scope.ExternalIdentities.SaveAsync(link with { LastSeenAt = link.LinkedAt.AddMinutes(1) });
            var before = (await scope.RevisionUsers.FindWithRevisionAsync("tenant-a", "owner"))!.Revision;
            var result = await scope.RevisionExternalIdentities.RemoveWithRevisionAsync(invalid == "issuer" ? new("tenant-a", "company", "https://different.example", "subject") : key,
                guard, saved.Revision);
            Assert.Equal(invalid == "issuer" ? ExternalIdentityRemovalStatus.Missing : ExternalIdentityRemovalStatus.Conflict, result.Status);
            Assert.NotNull(await scope.ExternalIdentities.FindByKeyAsync(key));
            Assert.Equal(before, (await scope.RevisionUsers.FindWithRevisionAsync("tenant-a", "owner"))!.Revision);
        }
        finally { DeleteDatabaseFiles(path); }
    }
}
