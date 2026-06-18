using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.OpenIddict;

public sealed class OpenIddictAuthenticationProviderModule(IOptions<OpenIddictIdentityOptions> options) : IAuthenticationProviderModule
{
    private OpenIddictIdentityOptions Options => options.Value;

    public string ProviderId => Options.ProviderId;

    public string DisplayName => Options.DisplayName;

    public string Kind => "openiddict";

    public ProviderCapabilities Capabilities => ProviderCapabilities.FoundationReference;

    public ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthenticationProviderDescriptor(
            Options.ProviderId,
            Options.DisplayName,
            Kind,
            Capabilities,
            Options.TenantId,
            Options.Enabled,
            Options.IsDefault));
}
