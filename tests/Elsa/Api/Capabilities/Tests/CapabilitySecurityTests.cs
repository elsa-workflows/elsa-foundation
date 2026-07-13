using System.Net;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.FastEndpoints.Features;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Oidc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Api.Capabilities.Tests;

public sealed class CapabilitySecurityTests
{
    [Fact]
    public async Task Secure_shell_rejects_an_unauthenticated_capability_bootstrap()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddCShellsAspNetCore(shells =>
        {
            shells
                .WithAuthenticationAndAuthorization()
                .WithAssemblies(
                    typeof(ApiCapabilitiesFeature).Assembly,
                    typeof(FastEndpointsFeature).Assembly,
                    typeof(FoundationIdentityAbstractionsFeature).Assembly,
                    typeof(OidcAuthenticationFeature).Assembly)
                .WithWebRouting(options => options.EnablePathRouting = true)
                .AddShell("tenant-a", shell => shell
                    .WithPath("tenant-a")
                    .WithFeature<FastEndpointsFeature>(feature => feature.EndpointRoutePrefix = string.Empty)
                    .WithFeature<ApiCapabilitiesFeature>()
                    .WithFeature<FoundationIdentityAbstractionsFeature>()
                    .WithFeature<OidcAuthenticationFeature>());
        });
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.MapShells();
        app.UseAuthentication();
        app.UseAuthorization();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var response = await client.GetAsync("/tenant-a/capabilities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public void Public_document_is_permission_and_caller_neutral()
    {
        var documentProperties = typeof(ApiCapabilitiesDocument).GetProperties().Select(x => x.Name).ToArray();
        var capabilityProperties = typeof(ApiCapabilityView).GetProperties().Select(x => x.Name).ToArray();
        var linkProperties = typeof(ApiCapabilityLinkView).GetProperties().Select(x => x.Name).ToArray();

        Assert.Equal(new[] { "Capabilities" }, documentProperties);
        Assert.DoesNotContain(capabilityProperties, name => name.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(linkProperties, name => name.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capabilityProperties, name => name.Contains("User", StringComparison.OrdinalIgnoreCase));
    }
}
