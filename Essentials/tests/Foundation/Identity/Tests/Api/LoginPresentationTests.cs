using System.Net;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.Api;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class LoginPresentationTests
{
    [Theory]
    [InlineData("field-guide", "field-guide-logo.png", true)]
    [InlineData("field-guide", "field-guide-logo.png", false)]
    [InlineData("parcel-room", "elsa-logo.png", true)]
    [InlineData("parcel-room", "elsa-logo.png", false)]
    public async Task Illustrated_configuration_serves_logo_and_art_under_both_prefixes(string theme, string logo, bool proxyPathBase)
    {
        await using var fixture = await TokenEndpointFixture.StartAsync(services =>
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Identity:Login:Layout"] = theme,
                    ["Identity:Login:Palette"] = theme,
                    ["Identity:Login:Artwork"] = theme
                }).Build()), pathBase: proxyPathBase ? "/tenant-a" : null,
            routePrefix: proxyPathBase ? null : "/tenant-a");
        var html = await fixture.Client.GetStringAsync("/tenant-a/_elsa/identity/login?returnUrl=/studio&error=1");
        Assert.Contains($"data-layout=\"{theme}\"", html);
        Assert.Contains($"data-palette=\"{theme}\"", html);
        Assert.Contains("name=\"color-scheme\" content=\"light\"", html);
        Assert.Contains("action=\"/tenant-a/_elsa/identity/login\"", html);
        Assert.Contains("name=\"__csrf\"", html);
        Assert.Contains("name=\"returnUrl\" value=\"/studio\"", html);
        Assert.Contains("role=\"alert\"", html);
        foreach (var (name, mime) in new[] { ($"{theme}.jpg", "image/jpeg"), (logo, "image/png") })
        {
            var url = $"/tenant-a/_elsa/identity/login-assets/{name}";
            Assert.Contains($"src=\"{url}\"", html);
            using var response = await fixture.Client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mime, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(bytes);
            if (mime == "image/jpeg")
            {
                Assert.True(bytes.Length > 10000);
                Assert.Equal(new byte[] { 0xff, 0xd8 }, bytes[..2]);
            }
            else
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync("/tenant-a/_elsa/identity/login-assets/arbitrary.png")).StatusCode);
    }

    [Theory]
    [InlineData("field-guide")]
    [InlineData("parcel-room")]
    public async Task Illustrated_login_keeps_real_csrf_protected_cookie_signin(string theme)
    {
        await using var fixture = await TokenEndpointFixture.StartAsync(services =>
            services.Configure<LoginPageOptions>(options =>
            {
                options.Layout = options.Palette = options.Artwork = theme;
            }));
        using var client = fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        Assert.Contains(client.DefaultRequestHeaders.GetValues("Cookie"), value => value.StartsWith("Elsa.Identity.Cookie=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Default_login_uses_pocket_orchestra_with_the_existing_form_first_layout()
    {
        await using var fixture = await TokenEndpointFixture.StartAsync();
        var html = await fixture.Client.GetStringAsync("/_elsa/identity/login");
        Assert.Contains("data-layout=\"parcel-room\"", html);
        Assert.Contains("data-palette=\"pocket-orchestra\"", html);
        Assert.Contains("src=\"/_elsa/identity/login-assets/pocket-orchestra.jpg\"", html);
        var response = await fixture.Client.GetAsync("/_elsa/identity/login-assets/pocket-orchestra.jpg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("river-to-sea")]
    [InlineData("open-architecture")]
    [InlineData("flowing-ribbons")]
    [InlineData("paper-landscape")]
    [InlineData("creative-studio")]
    [InlineData("botanical-growth")]
    [InlineData("constellation")]
    [InlineData("miniature-world")]
    [InlineData("glass-and-light")]
    [InlineData("unfolding-map")]
    [InlineData("parcel-room")]
    [InlineData("pocket-orchestra")]
    public async Task Configured_artwork_is_rendered_and_served_under_route_prefix(string artwork)
    {
        await using var fixture = await TokenEndpointFixture.StartAsync(services =>
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Identity:Login:Artwork"] = artwork }).Build()),
            routePrefix: "/tenant-a");
        var imageUrl = $"/tenant-a/_elsa/identity/login-assets/{artwork}.jpg";
        var html = await fixture.Client.GetStringAsync("/tenant-a/_elsa/identity/login");
        Assert.Contains($"src=\"{imageUrl}\"", html);
        var response = await fixture.Client.GetAsync(imageUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 10000);
        Assert.Equal(0xff, bytes[0]);
        Assert.Equal(0xd8, bytes[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shell_login_form_and_builtin_artwork_keep_the_request_path_base(bool proxyPathBase)
    {
        await using var fixture = await TokenEndpointFixture.StartAsync(pathBase:proxyPathBase ? "/tenant-a" : null,routePrefix:proxyPathBase ? null : "/tenant-a");
        var html = await fixture.Client.GetStringAsync("/tenant-a/_elsa/identity/login?returnUrl=/studio");
        Assert.Contains("action=\"/tenant-a/_elsa/identity/login\"",html);
        Assert.Contains("/tenant-a/_elsa/identity/login-assets/",html);
        Assert.Contains("src=\"/tenant-a/_elsa/identity/login-assets/elsa-logo.png\"",html);
        var logo = await fixture.Client.GetAsync("/tenant-a/_elsa/identity/login-assets/elsa-logo.png");
        Assert.Equal(HttpStatusCode.OK,logo.StatusCode);
        Assert.Equal("image/png",logo.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Default_login_uses_public_bundled_Elsa_logo()
    {
        await using var fixture = await TokenEndpointFixture.StartAsync();
        var html = await fixture.Client.GetStringAsync("/_elsa/identity/login");
        Assert.Contains("src=\"/_elsa/identity/login-assets/elsa-logo.png\"", html);
        var response = await fixture.Client.GetAsync("/_elsa/identity/login-assets/elsa-logo.png");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
    }

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
