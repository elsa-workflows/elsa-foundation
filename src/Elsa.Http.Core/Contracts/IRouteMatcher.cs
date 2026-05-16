using Microsoft.AspNetCore.Routing;

namespace Elsa.Http.Core.Contracts
{
    /// <summary>
    /// Matches a given request path against the specified route template.
    /// </summary>
    public interface IRouteMatcher
    {
        /// <summary>
        /// Matches a given request path against the specified route template.
        /// </summary>
        RouteValueDictionary? Match(string routeTemplate, string route);
    }
}
