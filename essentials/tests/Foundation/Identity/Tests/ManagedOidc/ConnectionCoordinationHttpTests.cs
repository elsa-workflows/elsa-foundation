using System.Net;
using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Managed_API_reports_read_only_when_the_host_cannot_coordinate_its_identity_store()
    {
        var catalog = new UncoordinatedCatalog();
        await using var host = await HttpHost.Start(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IConnectionCatalogStore>(catalog));
            services.AddFoundationExternalAuthentication();
        });
        using var browser = host.Browser(); await host.LoginAdministrator(browser);
        using (var read = await browser.GetAsync(Management))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var json = JsonDocument.Parse(await read.Content.ReadAsStreamAsync());
            Assert.False(json.RootElement.GetProperty("canManageConnections").GetBoolean());
            Assert.Equal("identity-coordination-unavailable", json.RootElement.GetProperty("managementUnavailableReason").GetString());
        }
        using var denied = await ManagementPost(host, browser, "/commands", new ConnectionWrite(ConnectionChange.Create, null, new() { Key = "draft" }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode); Assert.Equal(0, catalog.Saves);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("recovery-user")]
    [InlineData("recovery-membership")]
    [InlineData("recovery-framework")]
    [InlineData("role")]
    [InlineData("link")]
    public async Task Managed_catalog_save_rejects_an_independent_IAM_change_after_its_final_login_checks(string change)
    {
        await using var host = await HttpHost.Start();
        await MakeAdministrative(host, "company"); await MakeAdministrative(host, "partner");
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        using var company = host.Browser(); using var partner = host.Browser();
        IConnectionCatalogStore inner;
        await using (var seed = host.Scope())
        {
            inner = seed.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
            var records = (await seed.ServiceProvider.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections
                .Select(view => view.Record with { Id = "stored:" + view.Record.Key }).ToArray();
            Assert.True((await inner.SaveAsync(new(records))).Saved);
        }
        host.Options.Connections.Clear();
        var callback = await host.StartAndAuthorize(company);
        using (var returned = await company.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        callback = await host.StartAndAuthorize(partner, "partner");
        using (var returned = await partner.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var actorId = (await Session(company)).Subject!; var recoveryId = (await Session(partner)).Subject!;
        var before = await inner.ReadAsync();
        var catalog = new BeforeCatalogSave(inner, async () =>
        {
            await using var independent = host.Scope(); var services = independent.ServiceProvider;
            var users = services.GetRequiredService<IUserStore>();
            switch (change)
            {
                case "actor": Assert.True(await users.SetStatusAsync("default", actorId, UserStatus.Disabled)); break;
                case "recovery-user": Assert.True(await users.SetStatusAsync("default", recoveryId, UserStatus.Disabled)); break;
                case "recovery-membership":
                    await services.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("default", recoveryId,
                        TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string>())); break;
                case "recovery-framework":
                    var manager = services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
                    var frameworkUser = (await manager.FindByIdAsync(recoveryId))!;
                    Assert.True((await manager.SetLockoutEnabledAsync(frameworkUser, true)).Succeeded);
                    Assert.True((await manager.SetLockoutEndDateAsync(frameworkUser, DateTimeOffset.UtcNow.AddHours(1))).Succeeded); break;
                case "role":
                    var roleId = (await users.FindAsync("default", recoveryId))!.RoleIds.Single();
                    var roles = services.GetRequiredService<IRoleStore>();
                    await roles.SaveAsync((await roles.FindAsync("default", roleId))! with { Permissions = new HashSet<string>() }); break;
                case "link":
                    var links = services.GetRequiredService<IRevisionAwareExternalIdentityStore>();
                    var key = new ExternalIdentityKey("default", "partner", host.Partner.Issuer, "opaque-Subject");
                    var link = (await links.FindByKeyWithRevisionAsync(key))!;
                    var owner = (await services.GetRequiredService<IRevisionAwareUserStore>().FindRevisionGuardAsync("default", recoveryId))!;
                    Assert.Equal(ExternalIdentityRemovalStatus.Removed, (await links.RemoveWithRevisionAsync(key, owner, link.Revision)).Status); break;
            }
        });
        await using var node = host.RuntimeNode(catalog: catalog); await using var request = node.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextBinder>()
            .Bind(Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.Scoped(new("default")));
        var actor = await request.ServiceProvider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("default", actorId));
        var result = await request.ServiceProvider.GetRequiredService<IConnectionWriter>().ApplyAsync(
            new(ConnectionChange.Disable, before.Revision, before.Records.Single(record => record.Key == "company")), actor);
        Assert.Equal("revision-conflict", result.Failure);
        Assert.Equal(1, catalog.Saves);
        Assert.True(catalog.Guards >= 3); // Actor, recovery user and their real saved role definition.
        var after = await inner.ReadAsync(); Assert.Equal(before.Revision, after.Revision);
        Assert.All(after.Records, record => Assert.True(record.Enabled));
    }

    [Fact]
    public async Task Managed_draft_save_guards_the_existing_Elsa_roles_selected_for_provisioning()
    {
        await using var host = await HttpHost.Start(); using var browser = host.Browser();
        await host.LoginAdministrator(browser); var user = (await Session(browser)).Subject!;
        await using var current = host.Scope(); var inner = current.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
        var catalog = new BeforeCatalogSave(inner, async () =>
        {
            await using var independent = host.Scope(); var roles = independent.ServiceProvider.GetRequiredService<IRoleStore>();
            await roles.SaveAsync((await roles.FindAsync("default", "reader"))! with { Permissions = new HashSet<string> { "*" } });
        });
        await using var node = host.RuntimeNode(catalog: catalog); await using var request = node.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextBinder>()
            .Bind(Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.Scoped(new("default")));
        var actor = await request.ServiceProvider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("default", user));
        var result = await request.ServiceProvider.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Create, null,
            new() { Key = "draft", Admission = new() { Type = "jit", DefaultRoleIds = ["reader"] } }), actor);
        Assert.Equal("revision-conflict", result.Failure); Assert.Equal(1, catalog.Saves);
        Assert.Empty((await inner.ReadAsync()).Records);
    }

    private sealed class BeforeCatalogSave(IConnectionCatalogStore inner, Func<Task> change) : IConnectionCatalogStore
    {
        public bool SupportsIdentityRevisionGuards => inner.SupportsIdentityRevisionGuards;
        public int Saves { get; private set; }
        public int Guards { get; private set; }
        public ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default) => inner.ReadAsync(cancellationToken);
        public async ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default)
        {
            Saves++; Guards = catalog.IdentityRevisionGuards?.Count ?? 0;
            await change();
            return await inner.SaveAsync(catalog, cancellationToken);
        }
    }

    private sealed class UncoordinatedCatalog : IConnectionCatalogStore
    {
        public int Saves;
        public ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ConnectionCatalog([]));
        public ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default)
        { Saves++; return ValueTask.FromResult(new CatalogSaveResult(true)); }
    }
}
