using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Connections.Services;

/// <summary>
/// Named credentials declared in configuration under <c>Elsa:Credentials:&lt;name&gt;</c>. The default
/// kind is <c>configuration</c>; a host registers additional <see cref="IAccessTokenSource"/> kinds
/// (including cloud-vendor packages) without changing this type.
/// </summary>
internal sealed class ConfigurationAccessTokenSource(IConfiguration configuration, TimeProvider timeProvider) : IAccessTokenSource
{
    public const string KindName = "configuration";

    public string Kind => KindName;

    public ValueTask<AccessToken> GetTokenAsync(AccessTokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var section = configuration.GetSection($"{NamedCredentialOptionKeys.SectionName}:{request.Name}");
        var token = section[NamedCredentialOptionKeys.Token];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Named credential '{request.Name}' is not configured.");

        var tokenType = section[NamedCredentialOptionKeys.TokenType];
        if (string.IsNullOrWhiteSpace(tokenType))
            tokenType = "Bearer";

        return new(new AccessToken(tokenType, token, timeProvider.GetUtcNow().AddMinutes(5)));
    }
}
