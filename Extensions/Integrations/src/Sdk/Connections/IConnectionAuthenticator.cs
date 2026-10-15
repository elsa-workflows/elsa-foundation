namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Open authentication-scheme contract (plan D3). The core knows only this interface, never a
/// specific scheme. OAuth is one scheme among others; it does not live in this SDK.
/// </summary>
public interface IConnectionAuthenticator
{
    string Scheme { get; }

    ValueTask ApplyAsync(HttpRequestMessage request, IConnectionHandle connection, CancellationToken cancellationToken = default);
}
