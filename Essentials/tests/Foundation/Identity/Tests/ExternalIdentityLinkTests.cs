using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ExternalIdentityLinkTests
{
    [Fact]
    public async Task User_manager_links_existing_users_by_validated_issuer_without_changing_saved_roles()
    {
        var store = new InMemoryIdentityStore();
        IUserStore users = store;
        var user = new UserRecord("user", "tenant", "user", null, null, UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string> { "manually-assigned" }, new HashSet<string>());
        await users.SaveAsync(user);
        await users.SaveAsync(user with { Id = "other", UserName = "other" });
        var manager = new AspNetCoreIdentityUserManager(store, store);
        var request = new LinkExternalIdentityRequest("tenant", "company", "Subject", "user", ExternalIdentityLinkPolicy.Admin, false)
        {
            Issuer = "https://issuer/"
        };
        await manager.LinkExternalIdentityAsync(request);
        await manager.LinkExternalIdentityAsync(request with { Issuer = "https://different/", UserId = "other" });
        Assert.Equal("user", (await store.FindByKeyAsync(new("tenant", "company", request.Issuer, "Subject")))?.UserId);
        Assert.Null(await store.FindBySubjectAsync("tenant", "company", "Subject"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.LinkExternalIdentityAsync(request with { UserId = "other" }).AsTask());
        Assert.Equal(user.RoleIds, (await users.FindAsync("tenant", "user"))!.RoleIds);
    }

    [Fact]
    public async Task Qualified_link_storage_preserves_opaque_UTF16_without_case_or_unicode_normalization()
    {
        await using var database = new LinkDatabase();
        await using var db = await database.Open();
        await database.Seed(db, "user");
        var store = new EfExternalIdentityStore(db, database.Access);
        var subjects = new[] { "\u00e9", "e\u0301", "\ud800", "\ufffd", "a\0b", "a\0B" };
        foreach (var subject in subjects)
            await store.SaveAsync(Link("user") with { Issuer = "https://issuer/", ProviderSubject = subject });
        Assert.Equal(subjects.Length, await db.ExternalIdentities.CountAsync());
        foreach (var subject in subjects)
            Assert.Equal(subject, (await store.FindByKeyAsync(new("tenant", "company", "https://issuer/", subject)))?.ProviderSubject);
    }

    [Fact]
    public async Task In_memory_links_separate_exact_issuer_subject_and_legacy_namespace()
    {
        var store = new InMemoryIdentityStore();
        await AssertNamespaces(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Link("other") with { Issuer = "https://issuer/" }).AsTask());
        Assert.Equal("user", (await store.FindByKeyAsync(new("tenant", "company", "https://issuer/", "Subject")))?.UserId);
    }

    [Fact]
    public async Task Sqlite_links_survive_restart_and_framework_login_API_never_flattens_issuers()
    {
        await using var database = new LinkDatabase();
        await using (var db = await database.Open())
        {
            await database.Seed(db, "user");
            await database.Seed(db, "other");
            await AssertNamespaces(new EfExternalIdentityStore(db, database.Access));
            var framework = new EfCoreIdentityUserStore(db, database.Access);
            var user = (await framework.FindByIdAsync("user", default))!;
            var legacy = Assert.Single(await framework.GetLoginsAsync(user, default));
            Assert.Equal("Subject", legacy.ProviderKey);
            Assert.Equal("user", (await framework.FindByLoginAsync("company", "subject", default))?.Id);
            await framework.RemoveLoginAsync(user, "company", "subject", default);
            Assert.Empty(await framework.GetLoginsAsync(user, default));
            Assert.Equal(4, await db.ExternalIdentities.CountAsync());
        }
        await using (var restarted = await database.Open())
        {
            var store = new EfExternalIdentityStore(restarted, database.Access);
            Assert.Null(await store.FindBySubjectAsync("tenant", "company", "Subject"));
            Assert.Equal("user", (await store.FindByKeyAsync(new("tenant", "company", "https://issuer/", "Subject")))?.UserId);
            // Aggregate deletion validates and removes every qualified link, including differing issuer/case.
            Assert.True(await new EfUserStore(restarted, database.Access).DeleteAsync("tenant", "user"));
            Assert.Empty(await restarted.ExternalIdentities.ToListAsync());
        }
    }

    [Fact]
    public async Task Revision_rebind_targets_only_the_exact_link_and_unconditional_save_cannot_steal_it()
    {
        await using var database = new LinkDatabase();
        await using var db = await database.Open();
        await database.Seed(db, "user");
        await database.Seed(db, "other");
        var store = new EfExternalIdentityStore(db, database.Access);
        var first = Link("user") with { Issuer = "https://issuer/" };
        var second = first with { Issuer = "https://issuer" };
        Assert.Equal(IamRevisionSaveStatus.Saved, (await store.SaveWithRevisionAsync(first, null)).Status);
        Assert.Equal(IamRevisionSaveStatus.Saved, (await store.SaveWithRevisionAsync(second, null)).Status);
        var key = new ExternalIdentityKey("tenant", "company", first.Issuer!, first.ProviderSubject);
        var current = (await store.FindByKeyWithRevisionAsync(key))!;
        Assert.Equal(IamRevisionSaveStatus.Conflict, (await store.SaveWithRevisionAsync(first with { UserId = "other" }, null)).Status);
        await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => store.SaveAsync(first with { UserId = "other" }).AsTask());
        Assert.Equal(IamRevisionSaveStatus.Saved, (await store.SaveWithRevisionAsync(first with { UserId = "other" }, current.Revision)).Status);
        Assert.Equal("other", (await store.FindByKeyAsync(key))?.UserId);
        Assert.Equal("user", (await store.FindByKeyAsync(new("tenant", "company", second.Issuer!, second.ProviderSubject)))?.UserId);
        Assert.Equal(IamRevisionSaveStatus.Conflict, (await store.SaveWithRevisionAsync(first, current.Revision)).Status);
    }

    [Fact]
    public async Task Additive_migration_preserves_old_users_and_issuerless_links_without_guessing_an_issuer()
    {
        await using var database = new LinkDatabase();
        await using var db = await database.Open();
        await database.Seed(db, "user");
        var store = new EfExternalIdentityStore(db, database.Access);
        await store.SaveAsync(Link("user"));
        await db.Users.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, "1.0.0"));
        await db.ExternalIdentities.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SchemaVersion, "1.0.0"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928040656_Initial");
        await migrator.MigrateAsync();
        Assert.Equal("user", (await store.FindBySubjectAsync("tenant", "company", "subject"))?.UserId);
        Assert.Null(await store.FindByKeyAsync(new("tenant", "company", "https://issuer/", "Subject")));
        var users = new EfUserStore(db, database.Access);
        var user = (await users.FindAsync("tenant", "user"))!;
        await users.SaveAsync(user with { DisplayName = "Updated" });
        Assert.Equal(IdentityIamEfModule.SchemaVersion, (await db.Users.AsNoTracking().SingleAsync()).SchemaVersion);
        await store.SaveAsync(Link("user") with { Issuer = "https://issuer/" });
        Assert.Equal(2, await db.ExternalIdentities.CountAsync());
    }

    [Fact]
    public async Task A_corrupt_issuer_cannot_change_the_identity_found_under_a_scoped_key()
    {
        await using var database = new LinkDatabase();
        await using var db = await database.Open();
        await database.Seed(db, "user");
        var store = new EfExternalIdentityStore(db, database.Access);
        await store.SaveAsync(Link("user") with { Issuer = "https://issuer/" });
        await db.ExternalIdentities.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Issuer, "https://different/"));
        await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => store.FindByKeyAsync(new("tenant", "company", "https://issuer/", "Subject")).AsTask());
    }

    private static async Task AssertNamespaces(IExternalIdentityStore store)
    {
        var original = Link("user");
        await store.SaveAsync(original);
        foreach (var (issuer, subject) in new[]
                 {
                     ("https://issuer/", "Subject"), ("https://issuer/", "subject"),
                     ("https://issuer", "Subject"), ("https://ISSUER/", "Subject")
                 })
            await store.SaveAsync(original with { Issuer = issuer, ProviderSubject = subject });
        Assert.Equal(5, (await store.ListForUserAsync("tenant", "user")).Count);
        foreach (var record in await store.ListForUserAsync("tenant", "user"))
        {
            var found = record.Issuer is null
                ? await store.FindBySubjectAsync("tenant", "company", "subject")
                : await store.FindByKeyAsync(new("tenant", "company", record.Issuer, record.ProviderSubject));
            Assert.Equal(record, found);
        }
        Assert.Null(await store.FindByKeyAsync(new("tenant", "company", "https://issuer/", "SUBJECT")));
        Assert.Null(await store.FindByKeyAsync(new("tenant", "different", "https://issuer/", "Subject")));
    }

    internal static ExternalIdentityRecord Link(string user) => new("tenant", "company", "Subject", user, DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Admin);

    internal sealed class LinkDatabase : IAsyncDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), $"elsa-3579-links-{Guid.NewGuid():n}.db");
        internal readonly IPersistenceAccessContextAccessor Access = new FixedAccess();
        internal async Task<IdentityIamSqliteDbContext> Open(params IInterceptor[] interceptors)
        {
            var db = new IdentityIamSqliteDbContext(new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
                .UseSqlite($"Data Source={file};Pooling=False").AddInterceptors(interceptors).Options);
            await db.Database.MigrateAsync();
            return db;
        }
        internal ValueTask Seed(IdentityIamDbContext db, string id) => new EfUserStore(db, Access).SaveAsync(
            new(id, "tenant", id, null, id, UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string>()));
        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(file + suffix);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope("tenant"));
    }
}
