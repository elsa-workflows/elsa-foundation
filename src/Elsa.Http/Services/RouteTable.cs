using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;

namespace Elsa.Http.Services
{
    /// <inheritdoc />
    internal sealed class RouteTable(IMemoryCache cache, ILogger<RouteTable> logger) : IRouteTable
    {
        private static readonly object Key = new();

        private ConcurrentDictionary<string, HttpRouteData> Routes => cache.GetOrCreate(Key, _ => new ConcurrentDictionary<string, HttpRouteData>())!;

        /// <inheritdoc />
        public ValueTask Add(string route)
        {
            return Add(new HttpRouteData(route));
        }

        /// <inheritdoc />
        public ValueTask Add(HttpRouteData httpRouteData)
        {
            var route = httpRouteData.Route;
            var normalizedRoute = NormalizeRoute(route);

            if (route.Contains("//"))
            {
                logger.LogWarning("Path cannot contain double slashes. Ignoring path: {Path}", route);
                return ValueTask.CompletedTask;
            }

            if(Routes.ContainsKey(normalizedRoute))
            {
                throw new InvalidOperationException($"Route '{route}' is already added");
            }

            Routes.TryAdd(normalizedRoute, httpRouteData);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask Remove(string route)
        {
            var normalizedRoute = NormalizeRoute(route);
            Routes.TryRemove(normalizedRoute, out _);

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask AddRange(IEnumerable<string> routes)
        {
            foreach (var route in routes) 
                await Add(route);
        }

        /// <inheritdoc />
        public async ValueTask RemoveRange(IEnumerable<string> routes)
        {
            foreach (var route in routes) 
                await Remove(route);
        }

        /// <inheritdoc />
        public IEnumerator<HttpRouteData> GetEnumerator() => Routes.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        static string NormalizeRoute(string path) => $"/{path.Trim('/')}";

        public ValueTask Refresh(IEnumerable<string> routes)
        {
            Routes.Clear();
            return AddRange(routes);
        }

        public async ValueTask Refresh(IEnumerable<HttpRouteData> routes)
        {
            Routes.Clear();
            
            foreach(var route in routes)
            {
                await Add(route);
            }
        }
    }
}
