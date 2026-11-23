using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData("\"1\"")] [InlineData("{}")] [InlineData("9223372036854775808")]
    public async Task Mapped_API_returns_400_for_malformed_revision_before_any_write(string revision)
    {
        var fixture = new Fixture(); var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("StandaloneRequest")
            .AddScheme<AuthenticationSchemeOptions, StandaloneAuthenticationHandler>("StandaloneRequest", _ => { });
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "StandaloneRequest" });
        builder.Services.AddAuthorization(); builder.Services.AddSingleton(fixture.Management);
        builder.Services.AddSingleton<ITriggerRequestAccess>(fixture.Access);
        new StandaloneTriggersApiFeature().ConfigureServices(builder.Services);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); StandaloneTriggersApi.Map(app);
        await app.StartAsync(); using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/runtime/standalone-triggers/trigger-a/changes/preview",
            Element("{\"expectedRevision\":" + revision + ",\"command\":{\"kind\":\"PauseTrigger\"}}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("validation", problem.GetProperty("code").GetString());
        Assert.Equal(0, fixture.Store.Writes);
    }

    private sealed class StandaloneAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator"),
                new Claim(IdentityClaimTypes.Permission, TriggerPermissions.Manage), new Claim(IdentityClaimTypes.Normalized, "v1")], Scheme.Name)), Scheme.Name)));
    }
}
