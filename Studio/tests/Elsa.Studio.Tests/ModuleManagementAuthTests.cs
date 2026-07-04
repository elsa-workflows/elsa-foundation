using System.Net;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// Exercises the real <see cref="ModuleManagementAuth"/> wiring (API-key scheme + policy) end to end against a
/// TestServer that maps a probe endpoint behind <see cref="ModuleManagementAuth.PolicyName"/>, mirroring how the
/// module-management, feature-management, and console-stream surfaces are gated in Program.cs.
/// </summary>
public sealed class ModuleManagementAuthTests : IAsyncDisposable
{
    private const string ValidKey = "s3cr3t-management-key";

    private WebApplication? _app;

    [Fact]
    public async Task RejectsRequestWithoutApiKey()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsRequestWithWrongApiKey()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Add(ModuleManagementAuth.ApiKeyHeaderName, "not-the-key");

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestWithValidApiKey()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Add(ModuleManagementAuth.ApiKeyHeaderName, ValidKey);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FailsClosedWhenNoKeyConfiguredAndAnonymousNotAllowed()
    {
        var client = await StartGatedHostAsync(apiKey: null, allowAnonymous: false);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsAnonymousWhenNoKeyConfiguredAndOptOutEnabled()
    {
        // The development opt-out (defaulted true only in appsettings.Development.json) keeps local dev functional.
        var client = await StartGatedHostAsync(apiKey: null, allowAnonymous: true);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> StartGatedHostAsync(string? apiKey, bool allowAnonymous = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Studio:BackendModuleManagementApiKey"] = apiKey,
            ["Studio:AllowAnonymousManagementApi"] = allowAnonymous ? "true" : "false"
        };

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddModuleManagementAuth(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/gated", () => Results.Ok())
            .RequireAuthorization(ModuleManagementAuth.PolicyName);

        await app.StartAsync();
        _app = app;
        return app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
