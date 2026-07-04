using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using FastEndpoints;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>
/// <c>POST /_elsa/identity/login</c> — validates credentials via
/// <see cref="IIdentitySignInService"/> (<c>SignInManager.CheckPasswordSignInAsync</c>), issues the Identity
/// cookie, and returns the <c>AuthSession</c>. When a local <c>ReturnUrl</c> is supplied (the HTML-form
/// flow) a successful sign-in redirects to it instead; bad credentials yield 401.
/// </summary>
internal sealed class Login(IIdentitySignInService signInService) : ElsaEndpoint<LoginRequest>
{
    public override void Configure()
    {
        Post("/" + AspNetCoreIdentityDefaults.LoginRoute);
        AllowAnonymous();
        AllowFormData(urlEncoded: true);
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        // The HTML login page posts a (local) returnUrl; API callers post JSON without one.
        var isFormFlow = !string.IsNullOrEmpty(req.ReturnUrl);
        var outcome = await signInService.PasswordSignInAsync(req.Username, req.Password, req.TenantId, ct);

        if (!outcome.Succeeded)
        {
            if (isFormFlow)
            {
                // Re-present the login page with an error banner rather than a bare 401.
                var back = $"/{AspNetCoreIdentityDefaults.LoginRoute}?error=1&returnUrl={Uri.EscapeDataString(LocalUrl.Sanitize(req.ReturnUrl))}";
                await Send.RedirectAsync(back, isPermanent: false);
                return;
            }

            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (isFormFlow)
        {
            await Send.RedirectAsync(LocalUrl.Sanitize(req.ReturnUrl), isPermanent: false);
            return;
        }

        await Send.OkAsync(outcome.Session!, ct);
    }
}
