using System.Net;
using System.Text.RegularExpressions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Drives the real HTML-form login flow the way a browser does: GET the login page to obtain the antiforgery
/// token (embedded as the <c>__csrf</c> hidden field) and its paired cookie, then POST the credentials form
/// with both. This is a <b>legitimate</b> CSRF-protected sign-in — it does not rely on the old bypass of
/// omitting <c>returnUrl</c> to skip antiforgery validation — so the login→token→bearer integration flow still
/// passes for a real caller while the security gate stays enforced.
/// </summary>
internal static class LoginTestHelper
{
    private const string LoginRoute = "/_elsa/identity/login";

    /// <summary>
    /// Performs a legitimate form login as the seeded admin and attaches the issued identity cookie to
    /// <paramref name="client"/> for subsequent requests (TestServer's HttpClient does not persist cookies).
    /// </summary>
    public static async Task LoginAsync(HttpClient client)
    {
        // 1. GET the login page: yields the antiforgery request token (in the form) and the paired cookie.
        var pageResponse = await client.GetAsync(LoginRoute);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();

        var token = ExtractAntiforgeryToken(html);
        var antiforgeryCookie = ExtractSetCookie(pageResponse, prefix: ".AspNetCore.Antiforgery.");

        // 2. POST the credentials form WITH the antiforgery token + cookie — the CSRF-protected path.
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginRoute)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = IdentitySeeder.AdminUserName,
                ["password"] = IdentitySeeder.AdminPassword,
                [AspNetCoreIdentityDefaults.AntiforgeryFieldName] = token
            })
        };
        request.Headers.Add("Cookie", antiforgeryCookie);

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode}");

        var identityCookie = ExtractSetCookie(response, prefix: AspNetCoreIdentityDefaults.CookieScheme);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", identityCookie);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            $"name=\"{Regex.Escape(AspNetCoreIdentityDefaults.AntiforgeryFieldName)}\"\\s+value=\"([^\"]+)\"");
        if (!match.Success)
            throw new InvalidOperationException("The login page did not embed an antiforgery token.");

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractSetCookie(HttpResponseMessage response, string prefix)
    {
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))
            : null;
        if (cookie is null)
            throw new InvalidOperationException($"Expected a Set-Cookie starting with '{prefix}'.");

        // Keep only the name=value pair (drop attributes) for the outgoing Cookie header.
        return cookie.Split(';', 2)[0];
    }
}
