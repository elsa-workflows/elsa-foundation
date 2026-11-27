using System.Net;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Provider_changes_admission_and_preview_emit_closed_redacted_notifications_without_preview_admission()
    {
        var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services => services.AddSingleton<IAuthEventSink>(events));
        using var administrator = host.Browser(); using var browser = host.Browser();
        await host.LoginAdministrator(administrator);
        var before = await host.Counts();
        var previewCallback = await host.StartAndAuthorize(administrator, purpose: "preview");
        using (var completed = await administrator.GetAsync(previewCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Equal(before, await host.Counts());
        var previewNotice = Assert.Single(events.Items);
        Assert.Equal("preview.complete", previewNotice.Detail["action"]);
        Assert.Equal(AuthEventOutcome.Success, previewNotice.Outcome);
        Assert.DoesNotContain(events.Items, item => item.Detail["action"] == "signin.admit");

        var callback = await host.StartAndAuthorize(browser);
        using (var completed = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Contains(events.Items, item => item.Category == AuthEventCategory.Authentication && item.Detail["action"] == "signin.admit" && item.Outcome == AuthEventOutcome.Success);
        Assert.Contains(events.Items, item => item.Detail["action"] == "signin.complete" && item.Outcome == AuthEventOutcome.Success);

        var draft = host.Options.Connections[0] with { Id = "", Key = "audit-draft", Enabled = false, DisplayName = "display-name-private-canary" };
        var command = new ConnectionWrite(ConnectionChange.Create, null, draft);
        await ManagementChange(host, administrator, command);
        using (var stale = await ManagementPost(host, administrator, "/commands", command)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains(events.Items, item => item.Category == AuthEventCategory.IdentityChange && item.Detail["action"] == "connection.create" && item.Outcome == AuthEventOutcome.Success);
        Assert.Contains(events.Items, item => item.Detail["action"] == "connection.change" && item.Outcome == AuthEventOutcome.Denied);
        var serialized = JsonSerializer.Serialize(events.Items);
        foreach (var secret in new[] { "opaque-Subject", ControlledOidcServer.ClientSecret, "display-name-private-canary", "CredentialBinding", "id_token", "access_token" })
            Assert.DoesNotContain(secret, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.All(events.Items, item => Assert.All(item.Detail.Keys, key => Assert.Contains(key, new[] { "action", "result", "sessionId" })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_provider_callbacks_and_unbound_requests_emit_only_closed_outcomes(bool unbound)
    {
        var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services => services.AddSingleton<IAuthEventSink>(events));
        using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        var query = QueryHelpers.ParseQuery(callback.Query);
        var failed = QueryHelpers.AddQueryString(callback.GetLeftPart(UriPartial.Path), new Dictionary<string, string?>
        {
            ["state"] = unbound ? "unbound-state-private-canary" : query["state"].ToString(),
            ["error"] = "access_denied", ["error_description"] = "upstream-error-private-canary"
        });
        using var response = await browser.GetAsync(failed);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var notice = Assert.Single(events.Items);
        Assert.Equal(AuthEventOutcome.Denied, notice.Outcome);
        Assert.Equal("signin.complete", notice.Detail["action"]);
        Assert.Equal(unbound ? "request-refused" : "ProviderFailed", notice.Detail["result"]);
        Assert.Equal("anonymous", notice.Actor);
        Assert.DoesNotContain("private-canary", JsonSerializer.Serialize(notice), StringComparison.Ordinal);
        Assert.Equal(0, host.Company.TokenRequests);
        Assert.Equal(1, (await host.Counts()).Users);
    }
}
