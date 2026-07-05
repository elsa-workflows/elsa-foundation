using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// Surfaces the first-party ASP.NET Core Identity provider through <c>bootstrap</c>/<c>capabilities</c>.
/// Mirrors <c>OpenIddictAuthenticationProviderModule</c>, but carries a redirect challenge pointing at the
/// backend-served login page so the Studio bootstrap flow presents the out-of-the-box login experience.
/// </summary>
public sealed class AspNetCoreIdentityAuthenticationProviderModule(IOptions<AspNetCoreIdentityOptions> options) : IAuthenticationProviderModule
{
    private AspNetCoreIdentityOptions Options => options.Value;

    public string ProviderId => Options.ProviderId;

    public string DisplayName => Options.DisplayName;

    public string Kind => "password";

    public ProviderCapabilities Capabilities => ProviderCapabilities.FoundationReference;

    public ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthenticationProviderDescriptor(
            Options.ProviderId,
            Options.DisplayName,
            Kind,
            Capabilities,
            Options.TenantId,
            Options.Enabled,
            Options.IsDefault,
            new AuthenticationChallengeMetadata(
                Url: "/" + AspNetCoreIdentityDefaults.LoginRoute,
                Method: "GET",
                Scheme: AspNetCoreIdentityDefaults.CookieScheme)));
}
