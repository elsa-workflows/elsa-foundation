using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// A default <see cref="IHttpEndpointAuthorizationHandler"/> that allows all requests.
/// </summary>
internal sealed class AllowAnonymousHttpEndpointAuthorizationHandler : IHttpEndpointAuthorizationHandler
{
    /// <inheritdoc />
    public ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context) => new(true);
}