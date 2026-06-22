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
        var challenge = new AuthenticationChallengeMetadata(
            Options.ChallengePath,
            Scheme: Options.AuthenticationScheme,
            Parameters: new Dictionary<string, string> { ["returnUrl"] = "optional" });

        return ValueTask.FromResult(new AuthenticationProviderDescriptor(
            Options.ProviderId,
            Options.DisplayName,
            Kind,
            Capabilities,
            Options.TenantId,
            Options.Enabled,
            Options.IsDefault,
            challenge));
    }
}
