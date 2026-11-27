using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

internal sealed class ManagedConnectionProviderSource(IConnectionRegistry registry, IExternalAuthenticationFlowService flows, IConnectionReadiness readiness,
    IEnumerable<IExternalAuthenticationProtocol> protocols, IOptions<ExternalAuthenticationOptions> options, IOptions<FoundationIdentityOptions> identity,
    IAuthenticationSchemeProvider schemes,
    ICanonicalUserSignInService? signIn = null, IAuthenticationArtifactStore? artifacts = null, IHostEnvironment? environment = null)
    : IAuthenticationProviderSource
{
    internal static string ProviderId(string key) => "external:" + key;

    public async ValueTask<IReadOnlyList<AuthenticationProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (signIn is null || artifacts is null || !artifacts.IsDurable && environment?.IsDevelopment() != true ||
            await schemes.GetSchemeAsync(signIn.AuthenticationScheme) is null) return [];
        var result = new List<AuthenticationProviderDescriptor>();
        foreach (var view in (await registry.ReadAsync(cancellationToken)).Connections.Where(view => view.Effective && view.Record.Enabled && view.Record.ArchivedAt is null))
        {
            var matching = protocols.Where(protocol => protocol.Name == view.Record.Protocol).Take(2).ToArray();
            var callback = flows.GetCallbackUri(view.Record.Key, AuthenticationArtifactPurpose.SignIn);
            if (callback is null || matching.Length != 1 || !await readiness.IsReadyAsync(view.Record, cancellationToken)) continue;
            result.Add(new(ProviderId(view.Record.Key), string.IsNullOrWhiteSpace(view.Record.DisplayName) ? view.Record.Key : view.Record.DisplayName,
                view.Record.Protocol, identity.Value.ProviderCapabilities with { SupportsGroupSync = false }, options.Value.TenantId, true, view.Record.Preferred,
                new(callback[..^"/callback".Length], "POST", signIn.AuthenticationScheme)));
        }
        return result;
    }
}
