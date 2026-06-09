using Microsoft.AspNetCore.Routing;

namespace Elsa.Http.Core.Models;

public class HttpRouteData
{
    public HttpRouteData()
    {
    }

    public HttpRouteData(string route) : this()
    {
        Route = route;
    }

    public HttpRouteData(string route, RouteValueDictionary dataTokens) : this(route)
    {
        DataTokens = dataTokens;
    }

    public HttpRouteData(string route, RouteValueDictionary dataTokens, RouteValueDictionary routeValues) : this(route, dataTokens)
    {
        RouteValues = routeValues;
    }

    public string Route { get; set; } = default!;
    public RouteValueDictionary DataTokens { get; set; } = [];
    public RouteValueDictionary RouteValues { get; set; } = [];
}