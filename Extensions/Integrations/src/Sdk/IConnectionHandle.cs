using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Sdk;

/// <summary>
/// A short-lived, scoped handle the engine may hold. It never exposes long-lived secrets or tokens.
/// Preferred access is the authenticated <see cref="HttpClient"/>; <see cref="GetSensitiveAsync"/> is
/// the greppable escape hatch.
/// </summary>
public interface IConnectionHandle : IAsyncDisposable
{
    string TenantId { get; }

    string ConnectionName { get; }

    string ConnectionType { get; }

    string AuthScheme { get; }

    /// <summary>Non-secret settings only. Secrets stay behind <c>ISecretValueResolver</c>.</summary>
    IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>
    /// Hosts this connection may attach material to. Empty means no extra restriction beyond the scheme.
    /// </summary>
    IReadOnlyList<string> EgressAllowlist { get; }

    /// <summary>
    /// The named <c>Elsa.Activities.Http</c> client, with connection auth as a delegating handler
    /// on top of that client's handlers and header-free outbound logging (spec 188 FR-019).
    /// </summary>
    HttpClient HttpClient { get; }

    /// <summary>
    /// Resolves one sensitive field through <c>ISecretValueResolver</c> for this activation.
    /// The value is not cached on the handle.
    /// </summary>
    ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default);

    /// <summary>Secret field name to <see cref="SecretReference"/>, when the connection stores one.</summary>
    IReadOnlyDictionary<string, SecretReference> SecretFields { get; }
}
