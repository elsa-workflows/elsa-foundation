using Elsa.Http.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Contracts;

/// <summary>
/// Resolves the HTTP routes that map to a given workflow trigger path.
/// </summary>
public interface IHttpEndpointRoutesResolver
{
    /// <summary>
    /// Resolves the routes for the given path.
    /// </summary>
    Task<IEnumerable<HttpRouteData>> GetRoutes(string path);
}