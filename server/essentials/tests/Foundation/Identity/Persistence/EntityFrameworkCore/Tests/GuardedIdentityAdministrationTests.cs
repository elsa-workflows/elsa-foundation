using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

public sealed partial class IdentityProviderConfigurationEntityFrameworkCoreBehaviorTests
{
    [Theory]
    [InlineData("scope")]
    [InlineData("transaction")]
    [InlineData("pending")]
    [InlineData("split")]
    [InlineData("cross-tenant")]
    public async Task Guarded_link_administration_refuses_unowned_or_cross_scope_boundaries(string boundary)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var before = await fixture.Snapshots(); var guards = await fixture.Guards();
        var tenant = new FixedAccess(PersistenceAccessContext.Scoped(new("tenant-a")));
        var key = new ExternalIdentityKey("tenant-a", "company", "https://company.example", "subject");
        async Task Apply(EfExternalIdentityAdministrationStore store) => _ = await store.AttachAsync(
            new(key, before.User.Record, false, ExternalIdentityLinkPolicy.Admin), guards[0], guards, new(fixture.Configuration, fixture.Revision));
        var native = new EfExternalIdentityAdministrationStore(fixture.Iam, fixture.GlobalAccess, fixture.Providers);
        if (boundary == "scope")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(new(fixture.Iam, tenant, fixture.Providers)));
        if (boundary == "transaction")
        {
            await using var borrowed = await fixture.Iam.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(native));
            Assert.Same(borrowed, fixture.Iam.Database.CurrentTransaction);
        }
        if (boundary == "pending")
        {
            var row = await fixture.Iam.Users.SingleAsync(); row.DisplayName = "pending caller change";
            await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(native));
            Assert.True(fixture.Iam.ChangeTracker.HasChanges()); fixture.Iam.ChangeTracker.Clear();
        }
        if (boundary == "split")
        {
            await using var split = CreateContext(fixture.Path + ".split");
            var unsupported = new EfExternalIdentityAdministrationStore(fixture.Iam, fixture.GlobalAccess, split);
            Assert.False(unsupported.SupportsProviderRevisionGuards);
            await Assert.ThrowsAsync<NotSupportedException>(() => Apply(unsupported));
        }
        if (boundary == "cross-tenant")
        {
            guards[1] = new(IamRevisionGuardKind.Role, "another-tenant", guards[1].Id, guards[1].Revision);
            await Assert.ThrowsAsync<ArgumentException>(() => Apply(native));
        }
        var after = await fixture.Snapshots(); Assert.Equal(before.User.Revision, after.User.Revision);
        Assert.Equal(before.Role.Revision, after.Role.Revision); Assert.Equal(before.Stamp, after.Stamp);
        Assert.Empty(await fixture.Iam.ExternalIdentities.ToArrayAsync());
        Assert.Equal(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guarded_link_commit_acknowledgement_loss_is_reported_as_unknown_with_one_committed_unit(bool remove)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var links = new EfExternalIdentityStore(fixture.Iam, new FixedAccess(PersistenceAccessContext.Scoped(new("tenant-a"))));
        var key = new ExternalIdentityKey("tenant-a", "company", "https://company.example", "subject");
        if (remove) await links.SaveAsync(new("tenant-a", key.Provider, key.Subject, "recovery-user", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ExternalIdentityLinkPolicy.Admin) { Issuer = key.Issuer });
        var link = await links.FindByKeyWithRevisionAsync(key); var guards = await fixture.Guards(); var before = await fixture.Snapshots();
        await using var uncertain = new IdentityIamSqliteDbContext(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
            .UseSqlite(ConnectionTo(fixture.Path)).AddInterceptors(new CoordinatedCommitAcknowledgementLoss()).Options);
        var store = new EfExternalIdentityAdministrationStore(uncertain, fixture.GlobalAccess, fixture.Providers);
        await Assert.ThrowsAsync<EfCommitOutcomeUnknownException>(async () =>
        {
            if (remove) await store.RemoveAsync(key, guards[0], link!.Revision, guards, new(fixture.Configuration, fixture.Revision));
            else await store.AttachAsync(new(key, before.User.Record, false, ExternalIdentityLinkPolicy.Admin), guards[0], guards,
                new(fixture.Configuration, fixture.Revision));
        });
        Assert.Equal(!remove, await links.FindByKeyAsync(key) is not null);
        var after = await fixture.Snapshots(); Assert.NotEqual(before.User.Revision, after.User.Revision);
        Assert.NotEqual(before.Role.Revision, after.Role.Revision); Assert.Equal(before.Stamp, after.Stamp);
        Assert.NotEqual(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guarded_link_administration_commits_the_link_and_snapshots_and_preserves_sibling_credentials(bool managed)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var tenant = new FixedAccess(PersistenceAccessContext.Scoped(new("tenant-a")));
        var links = new EfExternalIdentityStore(fixture.Iam, tenant);
        await links.SaveAsync(new("tenant-a", "partner", "sibling", "recovery-user", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ExternalIdentityLinkPolicy.Admin) { Issuer = "https://partner.example" });
        var sibling = (await links.FindByKeyWithRevisionAsync(new("tenant-a", "partner", "https://partner.example", "sibling")))!;
        var before = await fixture.Snapshots();
        var store = new EfExternalIdentityAdministrationStore(fixture.Iam, managed ? fixture.GlobalAccess : tenant,
            managed ? fixture.Providers : null);
        Assert.True(store.SupportsIdentityRevisionGuards); Assert.Equal(managed, store.SupportsProviderRevisionGuards);
        var guards = await fixture.Guards();
        var key = new ExternalIdentityKey("tenant-a", "company", "https://company.example", "subject");
        var attached = await store.AttachAsync(new(key, before.User.Record, false, ExternalIdentityLinkPolicy.Admin), guards[0], guards,
            managed ? new(fixture.Configuration, fixture.Revision) : null);
        Assert.True(attached.Admitted); Assert.Equal("recovery-user", attached.UserId);
        var catalog = (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!;
        Assert.Equal(managed, catalog.Revision != fixture.Revision);
        var link = (await links.FindByKeyWithRevisionAsync(key))!;
        guards = await fixture.Guards();
        var removed = await store.RemoveAsync(key, guards[0], link.Revision, guards,
            managed ? new(catalog.Record, catalog.Revision) : null);
        Assert.Equal(ExternalIdentityRemovalStatus.Removed, removed.Status); Assert.Null(await links.FindByKeyAsync(key));
        Assert.Equal(sibling.Revision, (await links.FindByKeyWithRevisionAsync(new("tenant-a", "partner", "https://partner.example", "sibling")))!.Revision);
        Assert.Equal(before.Stamp, (await fixture.Snapshots()).Stamp);
        Assert.Equal(managed, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision != catalog.Revision);
    }

    [Theory]
    [InlineData(false, "user")]
    [InlineData(false, "role")]
    [InlineData(false, "catalog")]
    [InlineData(true, "user")]
    [InlineData(true, "role")]
    [InlineData(true, "catalog")]
    public async Task Guarded_link_mutations_refuse_stale_IAM_or_catalog_and_roll_back_every_guard(bool remove, string changed)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var tenant = new FixedAccess(PersistenceAccessContext.Scoped(new("tenant-a")));
        var links = new EfExternalIdentityStore(fixture.Iam, tenant);
        var key = new ExternalIdentityKey("tenant-a", "company", "https://company.example", "subject");
        if (remove) await links.SaveAsync(new("tenant-a", key.Provider, key.Subject, "recovery-user", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ExternalIdentityLinkPolicy.Admin) { Issuer = key.Issuer });
        var old = await fixture.Snapshots(); var guards = await fixture.Guards();
        var link = await links.FindByKeyWithRevisionAsync(key);
        if (changed == "user") await fixture.Users.SaveAsync(old.User.Record with { DisplayName = "Concurrent edit" });
        if (changed == "role") await fixture.Roles.SaveAsync(old.Role.Record with { Permissions = new HashSet<string> { "changed" } });
        if (changed == "catalog") await fixture.Store.SaveAsync(fixture.Configuration with { Enabled = false });
        var before = await fixture.Snapshots(); var catalog = (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!;
        var store = new EfExternalIdentityAdministrationStore(fixture.Iam, fixture.GlobalAccess, fixture.Providers);
        if (remove)
            Assert.Equal(ExternalIdentityRemovalStatus.Conflict, (await store.RemoveAsync(key, guards[0], link!.Revision, guards,
                new(fixture.Configuration, fixture.Revision))).Status);
        else
            Assert.False((await store.AttachAsync(new(key, old.User.Record, false, ExternalIdentityLinkPolicy.Admin), guards[0], guards,
                new(fixture.Configuration, fixture.Revision))).Admitted);
        Assert.Equal(remove, await links.FindByKeyAsync(key) is not null);
        var after = await fixture.Snapshots(); Assert.Equal(before.User.Revision, after.User.Revision);
        Assert.Equal(before.Role.Revision, after.Role.Revision); Assert.Equal(before.Stamp, after.Stamp);
        Assert.Equal(catalog.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_provider_save_fault_rolls_back_the_guarded_link_mutation_and_IAM_roots(bool remove)
    {
        await using var fixture = await CoordinatedFixture.OpenAsync();
        var links = new EfExternalIdentityStore(fixture.Iam, new FixedAccess(PersistenceAccessContext.Scoped(new("tenant-a"))));
        var key = new ExternalIdentityKey("tenant-a", "company", "https://company.example", "subject");
        if (remove) await links.SaveAsync(new("tenant-a", key.Provider, key.Subject, "recovery-user", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ExternalIdentityLinkPolicy.Admin) { Issuer = key.Issuer });
        var link = await links.FindByKeyWithRevisionAsync(key); var guards = await fixture.Guards(); var before = await fixture.Snapshots();
        await using var faulted = CreateContext(fixture.Path, new CoordinatedSaveFault());
        var store = new EfExternalIdentityAdministrationStore(fixture.Iam, fixture.GlobalAccess, faulted);
        await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(async () =>
        {
            if (remove) await store.RemoveAsync(key, guards[0], link!.Revision, guards, new(fixture.Configuration, fixture.Revision));
            else await store.AttachAsync(new(key, before.User.Record, false, ExternalIdentityLinkPolicy.Admin), guards[0], guards,
                new(fixture.Configuration, fixture.Revision));
        });
        Assert.Equal(remove, await links.FindByKeyAsync(key) is not null);
        var after = await fixture.Snapshots(); Assert.Equal(before.User.Revision, after.User.Revision);
        Assert.Equal(before.Role.Revision, after.Role.Revision); Assert.Equal(before.Stamp, after.Stamp);
        Assert.Equal(fixture.Revision, (await fixture.Store.FindGlobalWithRevisionAsync("external-authentication"))!.Revision);
    }
}
