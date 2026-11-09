using Elsa.Connections.Core.Contracts;

namespace Elsa.Integrations.Connectors.OAuth2;

/// <summary>
/// Builds the Authorization header scheme. RFC 6750 bearer is case-insensitive, so a missing,
/// "bearer", or any non-standard <c>token_type</c> is sent as <c>Bearer</c>.
/// <see cref="OAuth2SettingKeys.TokenHeaderScheme"/> overrides that for providers that need
/// another scheme. The raw token_type stays in the grant payload.
/// </summary>
internal static class OAuth2Authorization
{
    public const string BearerScheme = "Bearer";

    public static string HeaderScheme(IResolvedConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Settings.TryGetValue(OAuth2SettingKeys.TokenHeaderScheme, out var configured)
            && !string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return BearerScheme;
    }

    public static string AuthorizationValue(IResolvedConnection connection, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var scheme = HeaderScheme(connection);
        return accessToken.StartsWith($"{scheme} ", StringComparison.OrdinalIgnoreCase)
            ? accessToken
            : $"{scheme} {accessToken}";
    }
}
