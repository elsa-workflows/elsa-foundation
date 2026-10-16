using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The admin questions the EF application and credential stores answer (#2775): which applications are there,
/// which roles and status each carries, and removing an application or its credential. Every test talks to the
/// provider-neutral IAM contracts over one temporary SQLite database.
/// </summary>
public sealed class IdentityApplicationAdministrationEntityFrameworkCoreTests : IAsyncLifetime
{
    private const string Tenant = "tenant-a";
    private readonly TemporarySqliteDatabase database = new("identity-application-administration");
    private readonly List<IAsyncDisposable> disposables = [];
    private IServiceProvider services = null!;

    private IApplicationStore Applications => services.GetRequiredService<IApplicationStore>();
    private ICredentialStore Credentials => services.GetRequiredService<ICredentialStore>();

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
    public async Task Pages_are_bounded_ordered_by_application_id_and_limited_to_the_tenant()
    {
        foreach (var id in new[] { "app-z", "app-a", "app-m", "app-b" })
            await Applications.SaveAsync(Application(Tenant, id));
        await Open("tenant-b").GetRequiredService<IApplicationStore>().SaveAsync(Application("tenant-b", "app-c"));

        var firstPage = await Applications.ListPageAsync(Tenant, new IamPageRequest(skip: 0, take: 2));
        var secondPage = await Applications.ListPageAsync(Tenant, new IamPageRequest(skip: 2, take: 2));

        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(4, secondPage.TotalCount);
        Assert.Equal(["app-a", "app-b"], firstPage.Items.Select(application => application.Id));
        Assert.Equal(["app-m", "app-z"], secondPage.Items.Select(application => application.Id));
    }

    [Fact]
    public async Task Roles_and_status_round_trip_and_a_status_change_is_saved()
    {
        var application = Application(Tenant, "app-a") with { RoleIds = Set("role-b", "role-a") };
        await Applications.SaveAsync(application);

        await Applications.SaveAsync(application with { Status = ApplicationStatus.Disabled });

        var stored = Assert.IsType<ApplicationRecord>(await Applications.FindAsync(Tenant, application.Id));
        Assert.Equal(ApplicationStatus.Disabled, stored.Status);
        Assert.True(stored.RoleIds.SetEquals(["role-a", "role-b"]));
        Assert.Equal(stored.Status, Assert.Single((await Applications.ListPageAsync(Tenant, new IamPageRequest())).Items).Status);
    }

    [Fact]
    public async Task Delete_removes_the_application_and_reports_a_missing_one()
    {
        await Applications.SaveAsync(Application(Tenant, "app-a"));

        Assert.True(await Applications.DeleteAsync(Tenant, "APP-A"));

        Assert.Null(await Applications.FindAsync(Tenant, "app-a"));
        Assert.Equal(0, (await Applications.ListPageAsync(Tenant, new IamPageRequest())).TotalCount);
        Assert.False(await Applications.DeleteAsync(Tenant, "app-a"));
    }

    [Fact]
    public async Task Delete_removes_the_credential_and_reports_a_missing_one()
    {
        await Credentials.SaveAsync(new CredentialRecord(
            "credential-a", Tenant, CredentialSubjectType.Application, "app-a", CredentialKind.ApiKey, "hash", "sha256", CredentialStatus.Active, null));

        Assert.True(await Credentials.DeleteAsync(Tenant, "credential-a"));

        Assert.Null(await Credentials.FindAsync(Tenant, "credential-a"));
        Assert.False(await Credentials.DeleteAsync(Tenant, "credential-a"));
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

    private static ApplicationRecord Application(string tenantId, string id) => new(
        id, tenantId, $"client-{id}", $"Display {id}", ApplicationType.Confidential, ResourceOwnership.Foundation,
        Set(), Set(), Set("role-1"), ApplicationStatus.Active);

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
