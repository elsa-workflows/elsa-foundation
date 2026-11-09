using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Applies a connection's credential to an outbound HTTP request. Must refuse to attach material
/// when <see cref="AllowedHostsPolicy.Allows"/> is false for the request URI.
/// </summary>
public interface IHttpRequestAuthenticator
{
    string SchemeName { get; }

    ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default);
}
