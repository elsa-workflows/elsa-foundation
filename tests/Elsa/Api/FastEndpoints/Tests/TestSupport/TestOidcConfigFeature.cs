using CShells.Features;
using Elsa.Foundation.Identity.Oidc;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.FastEndpoints.Tests.TestSupport;

/// <summary>
/// Test-only shell feature that supplies a dummy OIDC configuration (client id + non-HTTPS local
/// authority) so the real <see cref="OidcAuthenticationFeature"/> validates and initialises without
/// a live identity provider. The OpenID Connect handler is an <c>IAuthenticationRequestHandler</c>
/// that is initialised for every request and validates <c>ClientId</c>, so an unconfigured OIDC
/// feature would fault every request with a 500 before authorization runs. Providing a dummy client
/// id lets the shell reach the intended 401 (secured) / 200 (relaxed) behaviour that the per-shell
/// security test asserts. No network call happens: JWT bearer only fetches metadata on token
/// validation, and unauthenticated calls are challenged (401) without a token.
/// </summary>
[ShellFeature(
    name: "TestOidcConfig",
    DisplayName = "Test OIDC Configuration",
    Description = "Supplies a dummy OIDC client id/authority so the OIDC feature initialises in tests without a live provider."
)]
public sealed class TestOidcConfigFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<OidcAuthenticationOptions>(options =>
        {
            options.ClientId = "elsa-test-client";
            options.Authority = "https://localhost/";
            options.RequireHttpsMetadata = false;
        });
    }
}
