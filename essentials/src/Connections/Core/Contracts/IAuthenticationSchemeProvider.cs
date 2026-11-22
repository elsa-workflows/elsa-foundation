using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// An open authentication-scheme plugin. Core never hard-codes a scheme list; connector packages
/// contribute OAuth, SigV4, mTLS, HMAC, or a custom handshake by registering another implementation.
/// </summary>
public interface IAuthenticationSchemeProvider
{
    AuthSchemeDescriptor Descriptor { get; }

    /// <summary>
    /// Optional check before a connection with this scheme is saved. Schemes that persist grants
    /// use it to refuse a read-only store early.
    /// </summary>
    ValueTask ValidateAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
