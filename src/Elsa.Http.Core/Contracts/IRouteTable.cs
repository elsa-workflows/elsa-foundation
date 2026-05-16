using Elsa.Http.Core.Models;

namespace Elsa.Http.Core.Contracts
{
    /// <summary>
    /// Stores a list of all routes.
    /// </summary>
    public interface IRouteTable : IEnumerable<HttpRouteData>
    {
        /// <summary>
        /// Adds a route to the table.
        /// </summary>
        /// <param name="route">The route to add.</param>
        ValueTask Add(string route);

        /// <summary>
        /// Adds a route to the table.
        /// </summary>
        /// <param name="httpRouteData">The route to add.</param>
        ValueTask Add(HttpRouteData httpRouteData);

        /// <summary>
        /// Removes a route from the table.
        /// </summary>
        /// <param name="route">The route to remove.</param>
        ValueTask Remove(string route);

        /// <summary>
        /// Adds a range of routes to the table.
        /// </summary>
        /// <param name="routes">The routes to add.</param>
        ValueTask AddRange(IEnumerable<string> routes);

        /// <summary>
        /// Refreshes the route table; clears the current table and initializes the table with the specified routes
        /// </summary>
        /// <param name="routes"></param>
        /// <returns></returns>
        ValueTask Refresh(IEnumerable<string> routes);

        /// <summary>
        /// Refreshes the route table; clears the current table and initializes the table with the specified <see cref="HttpRouteData"/>
        /// </summary>
        /// <param name="routes"></param>
        /// <returns></returns>
        ValueTask Refresh(IEnumerable<HttpRouteData> routes);

        /// <summary>
        /// Removes a range of routes from the table.
        /// </summary>
        /// <param name="routes">The routes to remove.</param>
        ValueTask RemoveRange(IEnumerable<string> routes);
    }
}
