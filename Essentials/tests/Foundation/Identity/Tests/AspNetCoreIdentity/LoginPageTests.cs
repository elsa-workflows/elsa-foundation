using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// The backend-served login page and its open-redirect guard. The page must be self-contained HTML (no CDN
/// assets) and must never echo a non-local return URL.
/// </summary>
public sealed class LoginPageTests
{
    [Fact]
    public void Render_Produces_SelfContained_Html_Form_Posting_To_Login()
    {
        var html = LoginPage.Render("/studio", showError: false);

        Assert.StartsWith("<!doctype html>", html.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<form", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("action=\"/_elsa/identity/login\"", html);
        Assert.Contains("name=\"username\"", html);
        Assert.Contains("name=\"password\"", html);
        Assert.Contains("value=\"/studio\"", html);

        // Self-contained: no external asset references.
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Render_Shows_Error_Banner_When_Requested()
    {
        Assert.Contains("Invalid username or password", LoginPage.Render("/", showError: true));
        Assert.DoesNotContain("Invalid username or password", LoginPage.Render("/", showError: false));
    }

    [Fact]
    public void Render_Embeds_The_Antiforgery_Token_As_A_Hidden_Field()
    {
        var html = LoginPage.Render("/studio", showError: false, antiforgeryToken: "csrf-token-value");

        Assert.Contains($"name=\"{Elsa.Foundation.Identity.AspNetCoreIdentity.AspNetCoreIdentityDefaults.AntiforgeryFieldName}\"", html);
        Assert.Contains("value=\"csrf-token-value\"", html);
    }

    [Fact]
    public void Render_Omits_The_Antiforgery_Field_When_No_Token_Is_Supplied()
    {
        var html = LoginPage.Render("/studio", showError: false);

        Assert.DoesNotContain($"name=\"{Elsa.Foundation.Identity.AspNetCoreIdentity.AspNetCoreIdentityDefaults.AntiforgeryFieldName}\"", html);
    }

    [Theory]
    [InlineData("/studio", true)]
    [InlineData("/", true)]
    [InlineData("/a/b/c?x=1", true)]
    [InlineData("//evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("https://evil.com", false)]
    [InlineData("http://evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LocalUrl_Accepts_Only_Local_Paths(string? url, bool expected)
    {
        Assert.Equal(expected, LocalUrl.IsLocal(url));
    }

    [Fact]
    public void LocalUrl_Sanitize_Falls_Back_To_Root_For_NonLocal()
    {
        Assert.Equal("/studio", LocalUrl.Sanitize("/studio"));
        Assert.Equal("/", LocalUrl.Sanitize("https://evil.com"));
        Assert.Equal("/", LocalUrl.Sanitize("//evil.com"));
    }

    [Fact]
    public void Sanitize_Honours_Absolute_Url_On_A_Trusted_Origin()
    {
        var allowed = new[] { "https://localhost:7030" };

        // The full cross-origin return URL (path + query) is preserved for a trusted Studio origin.
        Assert.Equal("https://localhost:7030/?authProviderId=aspnetcore-identity",
            LocalUrl.Sanitize("https://localhost:7030/?authProviderId=aspnetcore-identity", allowed));

        // Local paths still pass regardless of the allow list.
        Assert.Equal("/studio", LocalUrl.Sanitize("/studio", allowed));
    }

    [Theory]
    [InlineData("https://localhost:7030/back", new[] { "https://localhost:7030" }, true)]   // exact origin
    [InlineData("https://LOCALHOST:7030/back", new[] { "https://localhost:7030" }, true)]   // host case-insensitive
    [InlineData("https://localhost:7030/back", new[] { "https://localhost:7030/" }, true)]  // trailing slash tolerated
    [InlineData("https://localhost:7031/back", new[] { "https://localhost:7030" }, false)]  // wrong port
    [InlineData("http://localhost:7030/back", new[] { "https://localhost:7030" }, false)]   // wrong scheme
    [InlineData("https://evil.com/back", new[] { "https://localhost:7030" }, false)]        // wrong host
    [InlineData("javascript:alert(1)", new[] { "https://localhost:7030" }, false)]          // non-http scheme
    [InlineData("https://localhost:7030/back", new string[0], false)]                       // empty allow list
    public void IsTrustedAbsolute_Matches_Only_Allow_Listed_Origins(string url, string[] allowed, bool expected)
    {
        Assert.Equal(expected, LocalUrl.IsTrustedAbsolute(url, allowed));
        Assert.Equal(expected ? url : "/", LocalUrl.Sanitize(url, allowed));
    }

    [Theory]
    [InlineData("split", "linen", "ember")]
    [InlineData("reverse", "tidal", "tidal")]
    [InlineData("immersive", "daybreak", "daybreak")]
    [InlineData("editorial", "canopy", "canopy")]
    [InlineData("sketchbook", "sketchbook", "sketchbook")]
    public void Custom_presentation_keeps_form_security_and_encodes_branding(string layout, string palette, string artwork)
    {
        var html = LoginPage.Render("/studio?x=\"unsafe", true, "csrf\"token", new LoginPageOptions
        {
            Layout = layout, Palette = palette, Artwork = artwork,
            ProductName = "<script>alert(1)</script>", Tagline = "Calm & clear",
            LogoUrl = "javascript:alert(1)", ImageUrl = "data:text/html,unsafe", HelpUrl = "//unsafe.example", PrivacyUrl = "/privacy"
        });
        Assert.Contains($"data-layout=\"{layout}\"", html);
        Assert.Contains($"data-palette=\"{palette}\"", html);
        Assert.Contains($"/_elsa/identity/login-assets/{artwork}.jpg", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Calm &amp; clear", html);
        Assert.Contains("csrf&quot;token", html);
        Assert.Contains("name=\"username\"", html);
        Assert.Contains("name=\"password\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("data:text/html", html);
        Assert.DoesNotContain("//unsafe.example", html);
        Assert.DoesNotContain("<script>", html);
    }
}
