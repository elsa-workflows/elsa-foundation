using System.Net;
using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Theory]
    [InlineData("remaining-link")]
    [InlineData("editor-role")]
    [InlineData("remaining-role")]
    [InlineData("global-user")]
    [InlineData("catalog")]
    public async Task HTTP_unlink_rejects_changes_after_its_recovery_snapshot(string change)
    {
        HttpHost? active = null; string? user = null; string? editor = null; var changed = 0;
        await using var host = await HttpHost.Start(services =>
        {
            var original = services.Single(item => item.ServiceType == typeof(IExternalLinkRecovery));
            services.Replace(ServiceDescriptor.Scoped<IExternalLinkRecovery>(provider => new ChangingLinkRecovery(
                (IExternalLinkRecovery)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), async () =>
                {
                    changed++;
                    await using var independent = active!.Scope(); var scoped = independent.ServiceProvider;
                    if (change == "remaining-link")
                    {
                        var key = new ExternalIdentityKey("default", "partner", active.Partner.Issuer, "opaque-Subject");
                        var links = scoped.GetRequiredService<IRevisionAwareExternalIdentityStore>();
                        var link = (await links.FindByKeyWithRevisionAsync(key))!;
                        var owner = (await scoped.GetRequiredService<IRevisionAwareUserStore>().FindRevisionGuardAsync("default", user!))!;
                        Assert.Equal(ExternalIdentityRemovalStatus.Removed, (await links.RemoveWithRevisionAsync(key, owner, link.Revision)).Status);
                    }
                    else if (change == "global-user") Assert.True(await scoped.GetRequiredService<IUserStore>().SetStatusAsync("default", editor!, UserStatus.Disabled));
                    else if (change == "catalog")
                    {
                        var catalog = scoped.GetRequiredService<IConnectionCatalogStore>(); var current = await catalog.ReadAsync();
                        Assert.True((await catalog.SaveAsync(current with { Records = [new() { Id = "draft", Key = "unrelated" }] })).Saved);
                    }
                    else
                    {
                        var id = change == "remaining-role" ? "reader" : (await scoped.GetRequiredService<IUserStore>().FindAsync("default", editor!))!.RoleIds.Single();
                        var roles = scoped.GetRequiredService<IRoleStore>();
                        await roles.SaveAsync((await roles.FindAsync("default", id))! with { Permissions = new HashSet<string>() });
                    }
                })));
            services.EnsureReplacementContract<IExternalLinkRecovery, ChangingLinkRecovery>();
        });
        active = host;
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); editor = (await Session(administrator)).Subject!;
        user = await SignInCompanyAndPartner(host, company, partner);
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        using var denied = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company"));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode); Assert.Equal(1, changed);
        await using var currentScope = host.Scope();
        Assert.NotNull(await currentScope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().FindByKeyAsync(new("default", "company", host.Company.Issuer, "opaque-Subject")));
        if (change != "catalog")
            Assert.Equal(state.GetProperty("catalogRevision").GetString(), (await currentScope.ServiceProvider.GetRequiredService<IConnectionCatalogStore>().ReadAsync()).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_runtime_nodes_cannot_both_remove_the_users_proved_remaining_links(bool managed)
    {
        await using var host = await HttpHost.Start(databaseManagement: managed);
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); var editor = (await Session(administrator)).Subject!;
        var user = await SignInCompanyAndPartner(host, company, partner);
        await using var nodeA = host.RuntimeNode(); await using var nodeB = host.RuntimeNode();
        var arrivals = 0; var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = await Task.WhenAll(Remove(nodeA, "company", host.Company.Issuer), Remove(nodeB, "partner", host.Partner.Issuer));
        Assert.Single(results, result => result.Status == ExternalIdentityRemovalStatus.Removed);
        Assert.Single(results, result => result.Status == ExternalIdentityRemovalStatus.Conflict);
        Assert.Equal(2, arrivals);
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        var remaining = Assert.Single(state.GetProperty("items").EnumerateArray());
        using var refused = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, remaining.GetProperty("provider").GetString()!));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        async Task<ExternalIdentityRemovalResult> Remove(ServiceProvider node, string provider, string issuer)
        {
            await using var scope = node.CreateAsyncScope(); var services = scope.ServiceProvider;
            services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
            var actor = await services.GetRequiredService<IPrincipalFactory>().CreateAsync(new("default", editor));
            var current = (await services.GetRequiredService<IRevisionAwareUserStore>().FindWithRevisionAsync("default", user))!;
            var authorization = (await services.GetRequiredService<IExternalLinkAuthorizer>().CaptureAsync(actor, user, current.Revision))!;
            var key = new ExternalIdentityKey("default", provider, issuer, "opaque-Subject");
            var link = (await services.GetRequiredService<IRevisionAwareExternalIdentityStore>().FindByKeyWithRevisionAsync(key))!;
            var guards = await services.GetRequiredService<IExternalLinkRecovery>().CaptureAsync(actor, authorization, key);
            Assert.NotNull(guards);
            ProviderConfigurationWrite? write = null;
            if (managed)
            {
                var catalog = services.GetRequiredService<IConnectionCatalogStore>(); write = catalog.PrepareProviderWrite(await catalog.ReadAsync()); Assert.NotNull(write);
            }
            if (Interlocked.Increment(ref arrivals) == 2) captured.TrySetResult();
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using var commit = node.CreateAsyncScope();
            commit.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(managed
                ? PersistenceAccessContext.PrivilegedGlobal(new("test-two-node-unlink")) : PersistenceAccessContext.Scoped(new("default")));
            return await commit.ServiceProvider.GetRequiredService<IExternalIdentityAdministrationStore>().RemoveAsync(key, authorization.Owner, link.Revision, guards!, write);
        }
    }

    private sealed class ChangingLinkRecovery(IExternalLinkRecovery inner, Func<Task> change) : IExternalLinkRecovery
    {
        public async ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureAsync(ClaimsPrincipal actor, ExternalLinkAuthorization authorization,
            ExternalIdentityKey excluded, CancellationToken cancellationToken = default)
        {
            var guards = await inner.CaptureAsync(actor, authorization, excluded, cancellationToken);
            if (guards is not null) await change();
            return guards;
        }
    }
}
