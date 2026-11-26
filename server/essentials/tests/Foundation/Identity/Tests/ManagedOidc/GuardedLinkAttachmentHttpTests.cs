using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Theory]
    [InlineData("editor-role")]
    [InlineData("editor-status")]
    [InlineData("editor-membership")]
    [InlineData("target-role")]
    public async Task HTTP_attachment_rejects_authority_changes_after_the_final_delegation_snapshot(string change)
    {
        HttpHost? active = null; var changed = 0; string? editor = null;
        await using var host = await HttpHost.Start(services =>
        {
            var original = services.Single(descriptor => descriptor.ServiceType == typeof(IExternalLinkAuthorizer));
            services.Replace(ServiceDescriptor.Scoped<IExternalLinkAuthorizer>(provider => new ChangingLinkAuthorization(
                (IExternalLinkAuthorizer)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), async () =>
                {
                    changed++;
                    await using var independent = active!.Scope(); var scoped = independent.ServiceProvider;
                    if (change == "editor-status") Assert.True(await scoped.GetRequiredService<IUserStore>().SetStatusAsync("default", editor!, UserStatus.Disabled));
                    else if (change == "editor-membership")
                        await scoped.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("default", editor!, TenantMembershipStatus.Suspended,
                            new HashSet<string>(), new HashSet<string>()));
                    else
                    {
                        var id = change == "target-role" ? "reader" : (await scoped.GetRequiredService<IUserStore>().FindAsync("default", editor!))!.RoleIds.Single();
                        var roles = scoped.GetRequiredService<IRoleStore>();
                        await roles.SaveAsync((await roles.FindAsync("default", id))! with
                            { Permissions = change == "target-role" ? new HashSet<string> { "*" } : new HashSet<string>() });
                    }
                })));
            services.EnsureReplacementContract<IExternalLinkAuthorizer, ChangingLinkAuthorization>();
        });
        active = host;
        using var administrator = host.Browser(); await host.LoginAdministrator(administrator);
        editor = (await Session(administrator)).Subject!; await AddLinkTarget(host, "target");
        var state = await AccessDocument(administrator, "/users/target/links"); Assert.True(state.GetProperty("canAttach").GetBoolean());
        using var result = await ManagementPost(host, administrator, "/users/target/links/attach", AttachRequest(state));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.Equal(1, changed);
        Assert.Equal("revision-conflict", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("failure").GetString());
        await using var current = host.Scope();
        Assert.Null(await current.ServiceProvider.GetRequiredService<IExternalIdentityStore>().FindByKeyAsync(new("default", "company", host.Company.Issuer, "opaque-Subject")));
        Assert.Equal(state.GetProperty("catalogRevision").GetString(), (await current.ServiceProvider.GetRequiredService<IConnectionCatalogStore>().ReadAsync()).Revision);
    }

    private sealed class ChangingLinkAuthorization(IExternalLinkAuthorizer inner, Func<Task> change) : IExternalLinkAuthorizer
    {
        public async ValueTask<ExternalLinkAuthorization?> CaptureAsync(ClaimsPrincipal actor, string userId, string? expectedUserRevision = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.CaptureAsync(actor, userId, expectedUserRevision, cancellationToken);
            if (result is not null && expectedUserRevision is not null) await change();
            return result;
        }
    }
}
