using Elsa.Http.Core.Models;

namespace Elsa.Http.Core.Contracts;

/// <summary>
/// A handler that is invoked when authorizing an inbound HTTP request. Contract lives in <c>Elsa.Http.Core</c>
/// (spec 089 sub-unit C) so the request middleware can consume it without a cross-module edge; the default
/// implementations ship from <c>Elsa.Workflows.Runtime.Http</c>.
/// </summary>
public interface IHttpEndpointAuthorizationHandler
{
    /// <summary>
    /// Authorizes an inbound HTTP request.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>True if the request is authorized, otherwise false.</returns>
    ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context);
}
