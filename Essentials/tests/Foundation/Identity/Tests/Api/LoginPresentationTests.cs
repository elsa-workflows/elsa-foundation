using System.Net;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.Api;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class LoginPresentationTests
{
    [Fact]
    public async Task Presentation_replacement_receives_validated_return_and_real_security_fields()
    {
        LoginPageContext? received = null;
        await using var fixture = await TokenEndpointFixture.StartAsync(services =>
            services.Configure<LoginPageOptions>(options =>
            {
                options.ProductName = "Northstar";
                options.Renderer = context =>
                {
                    received = context;
                    return $"<!doctype html><html lang=\"en\"><title>Northstar</title><body><h1>Sign in</h1>{context.FormHtml}</body></html>";
                };
            }));
        var response = await fixture.Client.GetAsync("/_elsa/identity/login?returnUrl=https://untrusted.example/&error=1");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(received);
        Assert.Equal("/", received.ReturnUrl);
        Assert.True(received.ShowError);
        Assert.Equal("Northstar", received.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(received.AntiforgeryToken));
        Assert.Contains("name=\"__csrf\"", html);
        Assert.Contains("action=\"/_elsa/identity/login\"", html);
        Assert.Contains("value=\"/\"", html);
    }

    [Theory]
    [InlineData("ember.jpg")]
    [InlineData("tidal.jpg")]
    [InlineData("daybreak.jpg")]
    [InlineData("canopy.jpg")]
    public async Task Bundled_artwork_is_public_and_local(string name)
    {
        await using var fixture = await TokenEndpointFixture.StartAsync();
        var response = await fixture.Client.GetAsync($"/_elsa/identity/login-assets/{name}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 10000);
        Assert.Equal(0xff, bytes[0]);
        Assert.Equal(0xd8, bytes[1]);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync("/_elsa/identity/login-assets/unknown.jpg")).StatusCode);
    }
}
