namespace Elsa.Integrations.Sdk;

/// <summary>
/// A short-lived, scoped handle the engine may hold. It never exposes long-lived secrets or tokens.
/// </summary>
internal interface IConnectionHandle : IAsyncDisposable
{
    string TenantId { get; }
    string ConnectionName { get; }
    string ConnectionType { get; }
    string AuthScheme { get; }

    /// <summary>Non-secret settings only. Secrets stay behind <c>ISecretValueResolver</c>.</summary>
    IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>HTTP client whose handler injects a short-lived access token.</summary>
    HttpClient HttpClient { get; }
}
