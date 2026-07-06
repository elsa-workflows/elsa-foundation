using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>
/// <c>POST /_elsa/identity/login</c> — validates credentials via
/// <see cref="IIdentitySignInService"/> (<c>SignInManager.CheckPasswordSignInAsync</c>), issues the Identity
/// cookie, and returns the <c>AuthSession</c>. When a local <c>ReturnUrl</c> is supplied (the HTML-form
/// flow) a successful sign-in redirects to it instead; bad credentials yield 401.
/// </summary>
/// <remarks>
/// <para>
/// <b>CSRF gate.</b> The antiforgery decision is keyed off the request <b>Content-Type</b>, never off any
/// attacker-controllable body field. Every browser-form login POST — anything that is not genuinely
/// <c>application/json</c> — must carry the antiforgery token embedded by <see cref="LoginPage"/> (and the
/// paired cookie), validated here before credentials are checked. A cross-site simple-form POST cannot set
/// <c>application/json</c> without a CORS preflight, so a genuine JSON body remains exempt (first-party API
/// callers exchange same-origin credentials for a cookie). Previously the gate derived from the presence of
/// the body-supplied <c>ReturnUrl</c>, which let an attacker skip CSRF entirely by omitting it.
/// </para>
/// <para>
/// <b>Response shaping</b> (302 redirect for the form flow, 200 JSON session otherwise) still keys off
/// <c>ReturnUrl</c> — that is a response-format choice, not a security boundary.
/// </para>
/// </remarks>
internal sealed class Login(IIdentitySignInService signInService, IAntiforgery antiforgery, IOptions<AspNetCoreIdentityOptions> options) : ElsaEndpoint<LoginRequest>
{
    private ICollection<string> AllowedReturnOrigins => options.Value.AllowedReturnUrlOrigins;

    public override void Configure()
    {
        Post("/" + AspNetCoreIdentityDefaults.LoginRoute);
        AllowAnonymous();
        AllowFormData(urlEncoded: true);
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        // Response shaping (redirect vs JSON) keys off the (form-only) returnUrl — a format choice, not the
        // security boundary. The CSRF gate below is decided solely by Content-Type.
        var isFormFlow = !string.IsNullOrEmpty(req.ReturnUrl);

        // Security boundary: validate antiforgery on every non-JSON (browser-form) login POST. A genuine
        // application/json request cannot be forged cross-site by a simple form (it would require a CORS
        // preflight), so only that path is exempt — the decision never touches a body field.
        if (RequiresAntiforgery() && !await IsAntiforgeryValidAsync())
        {
            if (isFormFlow)
            {
                // Missing/invalid CSRF token on the form flow: re-present the login page with an error banner.
                var back = $"/{AspNetCoreIdentityDefaults.LoginRoute}?error=1&returnUrl={Uri.EscapeDataString(LocalUrl.Sanitize(req.ReturnUrl, AllowedReturnOrigins))}";
                await Send.RedirectAsync(back, isPermanent: false);
                return;
            }

            // A non-JSON, non-returnUrl form POST without a token: reject outright rather than authenticate.
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var outcome = await signInService.PasswordSignInAsync(req.Username, req.Password, req.TenantId, ct);

        if (!outcome.Succeeded)
        {
            if (isFormFlow)
            {
                // Re-present the login page with an error banner rather than a bare 401.
                var back = $"/{AspNetCoreIdentityDefaults.LoginRoute}?error=1&returnUrl={Uri.EscapeDataString(LocalUrl.Sanitize(req.ReturnUrl, AllowedReturnOrigins))}";
                await Send.RedirectAsync(back, isPermanent: false);
                return;
            }

            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (isFormFlow)
        {
            // The target is already constrained by LocalUrl.Sanitize to a same-origin path or an
            // allow-listed origin, so remote redirects are safe to permit here — a trusted cross-origin
            // Studio return URL would otherwise throw as "not local".
            await Send.RedirectAsync(LocalUrl.Sanitize(req.ReturnUrl, AllowedReturnOrigins), isPermanent: false, allowRemoteRedirects: true);
            return;
        }

        await Send.OkAsync(outcome.Session!, ct);
    }

    /// <summary>
    /// True for every login POST that is not a genuine <c>application/json</c> request. Browser HTML-form
    /// submissions (<c>application/x-www-form-urlencoded</c>, <c>multipart/form-data</c>, <c>text/plain</c>)
    /// and requests with no/unknown Content-Type all require a validated antiforgery token; only real JSON
    /// bodies — which a cross-site simple form cannot produce without a CORS preflight — are exempt.
    /// </summary>
    private bool RequiresAntiforgery()
    {
        var contentType = HttpContext.Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
            return true;

        // Match the media type only (ignore charset/boundary parameters) and require an exact application/json.
        return !MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            || !parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsAntiforgeryValidAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
