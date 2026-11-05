using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Agent.Specialists.Api;
using Elsa.Agent.Specialists.Connections;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Api.Capabilities;
using Elsa.Connections.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Authoring;

/// <summary>The editor's model reads: only providers that can back a specialist, and the same capabilities publication consults.</summary>
public sealed partial class SpecialistsApiHttpTests : IAsyncLifetime
{
    internal const string Scheme = "SpecialistsApiTest";
    private const string Author = "activities/design:author";
    private const string Providers = "/agent/specialists/model-providers";
    private const string Capabilities = "/agent/specialists/model-capabilities";
    private IHost host = null!;

    public async Task InitializeAsync()
    {
        await sourceConnection.OpenAsync();
        await using (var sourceDb = new AgentSourceTestContext(sourceConnection)) await sourceDb.Database.EnsureCreatedAsync();
        host = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging();
                AddSourceStorage(services);
                services.AddRouting();
                services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(Scheme, _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>([Scheme], StringComparer.Ordinal));
                new ApiCapabilitiesFeature().ConfigureServices(services);
                new SpecialistsApiFeature().ConfigureServices(services);
                services.AddSingleton<IConnectionTypeProvider>(new SpecialistModelConnectionTypeProvider("fixture", "specialist-fixture"));
                // A connection type without a capability owner, and a provider key with two owners, cannot back a specialist.
                services.AddSingleton<IConnectionTypeProvider>(new SpecialistModelConnectionTypeProvider("orphan", "specialist-orphan"));
                services.AddSingleton<IConnectionTypeProvider>(new SpecialistModelConnectionTypeProvider("twice", "specialist-twice"));
                services.AddSingleton<ISpecialistModelCapabilitiesSource>(new FixtureCapabilities("fixture"));
                services.AddSingleton<ISpecialistModelCapabilitiesSource>(new FixtureCapabilities("twice"));
                services.AddSingleton<ISpecialistModelCapabilitiesSource>(new FixtureCapabilities("twice"));
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(SpecialistsApi.MapSpecialistsApi);
            });
        }).Build();
        await host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
        await sourceConnection.DisposeAsync();
    }

    [Fact]
    public async Task Only_a_provider_with_one_capability_owner_and_one_connection_type_is_offered()
    {
        var response = await SendAsync(HttpMethod.Get, Providers, null, Author);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        Assert.Equal("fixture", item.GetProperty("providerKey").GetString());
        Assert.Equal("specialist-fixture", item.GetProperty("connectionTypeName").GetString());
    }

    [Fact]
    public async Task Capabilities_come_from_the_owning_source_for_the_exact_model_reference()
    {
        var response = await SendAsync(HttpMethod.Post, Capabilities, """{"providerKey":"fixture","connectionName":"writer","modelId":"model-a"}""", Author);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(view.GetProperty("supportsJsonSchema").GetBoolean());
        Assert.Equal(4096, view.GetProperty("maximumOutputTokens").GetInt32());
        Assert.Equal("number", view.GetProperty("settingsSchema").GetProperty("properties").GetProperty("temperature").GetProperty("type").GetString());
        Assert.False(view.TryGetProperty("fingerprint", out _));
    }

    [Theory]
    [InlineData("""{"providerKey":"fixture","connectionName":"missing","modelId":"model-a"}""")]
    [InlineData("""{"providerKey":"fixture","connectionName":"writer","modelId":"model-b"}""")]
    [InlineData("""{"providerKey":"twice","connectionName":"writer","modelId":"model-a"}""")]
    [InlineData("""{"providerKey":"orphan","connectionName":"writer","modelId":"model-a"}""")]
    [InlineData("""{"providerKey":"fixture","connectionName":" ","modelId":"model-a"}""")]
    public async Task An_unavailable_or_mismatched_model_is_refused_without_saying_why(string body)
    {
        var response = await SendAsync(HttpMethod.Post, Capabilities, body, Author);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("missing", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_member_is_refused()
    {
        var response = await SendAsync(HttpMethod.Post, Capabilities, """{"providerKey":"fixture","connectionName":"writer","modelId":"model-a","apiKey":"x"}""", Author);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reading_activity_designs_alone_does_not_grant_the_model_reads()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, Providers, null, "activities/design:read")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, Capabilities, """{"providerKey":"fixture","connectionName":"writer","modelId":"model-a"}""", "activities/design:read")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, Providers, null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, Providers, null, "activities/design:manage")).StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, string? permission)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (permission is not null) request.Headers.Add("X-Permissions", permission);
        return await host.GetTestClient().SendAsync(request);
    }

    private sealed class FixtureCapabilities(string providerKey) : ISpecialistModelCapabilitiesSource
    {
        public string ProviderKey => providerKey;

        public ValueTask<SpecialistModelCapabilities> GetCapabilitiesAsync(SpecialistModelReference model, CancellationToken cancellationToken = default) =>
            model is { ConnectionName: "writer", ModelId: "model-a" }
                ? ValueTask.FromResult(new SpecialistModelCapabilities("fingerprint", JsonSerializer.SerializeToElement(new
                {
                    type = "object", additionalProperties = false, properties = new { temperature = new { type = "number", minimum = 0, maximum = 1 } }
                }), true, 4096))
                : throw new InvalidOperationException("missing connection");
    }

    private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permissions", out var permissions))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "subject-ada"),
                new(IdentityClaimTypes.TenantId, "tenant-a"),
                new(IdentityClaimTypes.Normalized, "v1")
            };
            claims.AddRange(permissions.ToString().Split(',').Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SpecialistsApiHttpTests.Scheme)), SpecialistsApiHttpTests.Scheme)));
        }
    }
}
