using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc;

public sealed class OidcAuthenticationProviderModule(IOptions<OidcAuthenticationOptions> options) : IAuthenticationProviderModule
{
    private OidcAuthenticationOptions Options => options.Value;

    public string ProviderId => Options.ProviderId;

    public string DisplayName => Options.DisplayName;

    public string Kind => "external-oidc";

    public ProviderCapabilities Capabilities => ProviderCapabilities.ExternalOidcDefault;

    public ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default)
    {
        // The interactive OpenID Connect handler is only registered once a ClientId is configured (see
        // AddFoundationIdentityOidc — registering it unconfigured faults every request). Until then this
        // provider can validate external bearer tokens but cannot be interactively challenged: advertising a
        // challenge to its (unregistered) scheme would 500 the challenge endpoint. So while unconfigured we
        // surface no challenge — which makes /challenge/oidc a clean 404 (existing guard) — and can never be
        // the default interactive provider (you cannot default to a provider you cannot challenge).
        var interactive = !string.IsNullOrWhiteSpace(Options.ClientId);

        var challenge = interactive
            ? new AuthenticationChallengeMetadata(
                Options.ChallengePath,
                Scheme: Options.AuthenticationScheme,
                Parameters: new Dictionary<string, string> { ["returnUrl"] = "optional" })
            : null;

        return ValueTask.FromResult(new AuthenticationProviderDescriptor(
            Options.ProviderId,
            Options.DisplayName,
            Kind,
            Capabilities,
            Options.TenantId,
            Options.Enabled,
            Options.IsDefault && interactive,
            challenge));
    }
}
