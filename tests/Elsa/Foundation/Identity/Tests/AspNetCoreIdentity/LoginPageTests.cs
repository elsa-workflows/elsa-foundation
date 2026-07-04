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
}
