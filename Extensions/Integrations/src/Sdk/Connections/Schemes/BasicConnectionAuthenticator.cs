using System.Net.Http.Headers;
using System.Text;

namespace Elsa.Integrations.Sdk.Connections.Schemes;

/// <summary>
/// Applies HTTP Basic auth. Username is the non-secret <c>username</c> setting; password is the
/// <c>password</c> secret field.
/// </summary>
public sealed class BasicConnectionAuthenticator : IConnectionAuthenticator
{
    public const string UsernameSetting = "username";
    public const string PasswordField = "password";

    public string Scheme => ConnectionAuthSchemes.Basic;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IConnectionHandle connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connection);

        if (!connection.Settings.TryGetValue(UsernameSetting, out var username) || string.IsNullOrEmpty(username))
        {
            throw new ConnectionUnavailableException(
                $"Connection '{connection.ConnectionName}' has no '{UsernameSetting}' setting for basic auth.",
                connection.ConnectionName);
        }

        var password = await connection.GetSensitiveAsync(PasswordField, cancellationToken);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password.Value}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }
}
