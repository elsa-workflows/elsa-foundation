using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// An open authentication-scheme plugin. Core never hard-codes a scheme list; connector packages
/// contribute OAuth, SigV4, mTLS, HMAC, or a custom handshake by registering another implementation.
/// </summary>
public interface IAuthenticationSchemeProvider
{
    AuthSchemeDescriptor Descriptor { get; }
}
