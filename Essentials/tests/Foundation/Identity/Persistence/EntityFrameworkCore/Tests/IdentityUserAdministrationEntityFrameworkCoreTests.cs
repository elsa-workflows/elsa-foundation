using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The admin questions the EF user store answers (#2774): who is there, find this person, turn the account off,
/// remove it. Every test talks to the provider-neutral IAM contracts over one temporary SQLite database.
/// </summary>
public sealed class IdentityUserAdministrationEntityFrameworkCoreTests : IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private readonly TemporarySqliteDatabase database = new("identity-user-administration");
    private readonly List<IAsyncDisposable> disposables = [];
    private IServiceProvider services = null!;

    private IUserStore Users => services.GetRequiredService<IUserStore>();
    private IPagedUserStore Pages => services.GetRequiredService<IPagedUserStore>();

    public async Task InitializeAsync()
    {
        services = Open(Tenant);
        await services.GetRequiredService<IdentityIamDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        // Scopes before their providers, then the database file.
        for (var index = disposables.Count - 1; index >= 0; index--)
            await disposables[index].DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task Pages_are_bounded_stably_ordered_and_limited_to_the_tenant()
    {
        foreach (var id in new[] { "user-z", "user-a", "user-m", "user-b" })
            await Users.SaveAsync(User(Tenant, id, id));
        await Open("tenant-b").GetRequiredService<IUserStore>().SaveAsync(User("tenant-b", "user-c", "user-c"));

        var firstPage = await Pages.ListPageAsync(Tenant, new IamPageRequest(skip: 0, take: 2));
        var secondPage = await Pages.ListPageAsync(Tenant, new IamPageRequest(skip: 2, take: 2));

        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(4, secondPage.TotalCount);
        Assert.Equal(["user-a", "user-b"], firstPage.Items.Select(user => user.Id));
        Assert.Equal(["user-m", "user-z"], secondPage.Items.Select(user => user.Id));
    }

    [Theory]
    [InlineData("ADA", "user-ada")]
    [InlineData("Ada.Lovelace@Example.Test", "user-ada")]
    [InlineData("bob@example.test", "user-bob")]
    public async Task Search_matches_a_whole_user_name_or_email_ignoring_case(string search, string expectedId)
    {
        await SaveSearchableUsersAsync();

        var page = await Pages.ListPageAsync(Tenant, new IamPageRequest(), search);

        Assert.Equal(expectedId, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalCount);
    }

    [Theory]
    [InlineData("ad")]
    [InlineData("example.test")]
    public async Task Search_does_not_match_part_of_a_value(string search)
    {
        await SaveSearchableUsersAsync();

        var page = await Pages.ListPageAsync(Tenant, new IamPageRequest(), search);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_blank_search_lists_every_user(string? search)
    {
        await SaveSearchableUsersAsync();

        var page = await Pages.ListPageAsync(Tenant, new IamPageRequest(), search);

        Assert.Equal(["user-ada", "user-adam", "user-bob"], page.Items.Select(user => user.Id));
    }

    [Fact]
    public async Task Disable_and_enable_change_only_the_status()
    {
        var user = User(Tenant, "user-a", "Ada", "ada@example.test");
        await Users.SaveAsync(user);

        Assert.True(await Users.SetStatusAsync(Tenant, user.Id, UserStatus.Disabled));
        Assert.Equal(user with { Status = UserStatus.Disabled }, await FindAsync(user.Id), UserRecordComparer.Instance);

        Assert.True(await Users.SetStatusAsync(Tenant, user.Id, UserStatus.Active));
        Assert.Equal(user, await FindAsync(user.Id), UserRecordComparer.Instance);
    }

    [Fact]
    public async Task A_status_change_moves_the_revision_so_a_stale_save_cannot_undo_it()
    {
        var user = User(Tenant, "user-a", "Ada", "ada@example.test");
        await Users.SaveAsync(user);
        var revisions = services.GetRequiredService<IRevisionAwareUserStore>();
        var stale = (await revisions.FindWithRevisionAsync(Tenant, user.Id))!;

        await Users.SetStatusAsync(Tenant, user.Id, UserStatus.Disabled);
        var result = await revisions.SaveWithRevisionAsync(stale.Record with { DisplayName = "Ada L." }, stale.Revision);

        Assert.Equal(IamRevisionSaveStatus.Conflict, result.Status);
        Assert.Equal(UserStatus.Disabled, (await FindAsync(user.Id)).Status);
    }

    [Fact]
    public async Task Status_change_and_delete_report_a_missing_user()
    {
        Assert.False(await Users.SetStatusAsync(Tenant, "missing", UserStatus.Disabled));
        Assert.False(await Users.DeleteAsync(Tenant, "missing"));
    }

    [Fact]
    public async Task Delete_removes_the_user_with_its_logins_and_membership_and_frees_its_name()
    {
        var user = User(Tenant, "user-a", "Ada", "ada@example.test");
        await Users.SaveAsync(user);
        var externalIdentities = services.GetRequiredService<IExternalIdentityStore>();
        await externalIdentities.SaveAsync(new ExternalIdentityRecord(
            Tenant, "google", "subject-a", user.Id, DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Auto));
        var memberships = services.GetRequiredService<ITenantMembershipStore>();
        await memberships.SaveAsync(new TenantMembershipRecord(Tenant, user.Id, TenantMembershipStatus.Active, Set(), Set()));

        Assert.True(await Users.DeleteAsync(Tenant, user.Id));

        Assert.Null(await Users.FindAsync(Tenant, user.Id));
        Assert.Null(await externalIdentities.FindBySubjectAsync(Tenant, "google", "subject-a"));
        Assert.Null(await memberships.FindAsync(Tenant, user.Id));
        Assert.Equal(0, (await Pages.ListPageAsync(Tenant, new IamPageRequest())).TotalCount);
        Assert.False(await Users.DeleteAsync(Tenant, user.Id));

        // The deleted user's name reservation went with it, so a new account can take the name.
        await Users.SaveAsync(User(Tenant, "user-b", "Ada", "ada@example.test"));
        Assert.Equal("user-b", Assert.Single((await Pages.ListPageAsync(Tenant, new IamPageRequest(), "ada")).Items).Id);
    }

    private IServiceProvider Open(string tenantId)
    {
        var collection = new ServiceCollection();
        collection.AddPersistenceCore(tenantId);
        collection.AddIdentityIamEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database.ConnectionString });
        var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        disposables.Add(provider);
        disposables.Add(scope);
        return scope.ServiceProvider;
    }

    private async Task SaveSearchableUsersAsync()
    {
        await Users.SaveAsync(User(Tenant, "user-ada", "Ada", "ada.lovelace@example.test"));
        await Users.SaveAsync(User(Tenant, "user-adam", "Adam", "adam@example.test"));
        await Users.SaveAsync(User(Tenant, "user-bob", "Bob", "bob@example.test"));
    }

    private async Task<UserRecord> FindAsync(string userId) => Assert.IsType<UserRecord>(await Users.FindAsync(Tenant, userId));

    private static UserRecord User(string tenantId, string id, string name, string? email = null) => new(
        id, tenantId, name, email, $"{name} Example", UserStatus.Active, ResourceOwnership.Foundation, Set("role-1"), Set("identity/users:read"));

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    /// <summary>Compares records by value, including the contents of their role and permission sets.</summary>
    private sealed class UserRecordComparer : IEqualityComparer<UserRecord>
    {
        public static readonly UserRecordComparer Instance = new();

        public bool Equals(UserRecord? x, UserRecord? y) =>
            x is not null && y is not null &&
            x with { RoleIds = y.RoleIds, DirectPermissions = y.DirectPermissions } == y &&
            x.RoleIds.SetEquals(y.RoleIds) && x.DirectPermissions.SetEquals(y.DirectPermissions);

        public int GetHashCode(UserRecord obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
