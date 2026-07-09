using System.Collections;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// <see cref="IRouteMatcher"/> over the real ASP.NET <see cref="TemplateParser"/>/<see cref="TemplateMatcher"/>
/// — a verbatim copy of the production <c>Elsa.Http.Services.RouteMatcher</c> (which is internal to Elsa.Http).
/// Matching semantics are therefore identical to production.
/// </summary>
internal sealed class TestRouteMatcher : IRouteMatcher
{
    public RouteValueDictionary? Match(string routeTemplate, string route)
    {
        var template = TemplateParser.Parse(routeTemplate);
        var matcher = new TemplateMatcher(template, GetDefaults(template));
        var values = new RouteValueDictionary();

        return matcher.TryMatch(route, values) ? values : null;
    }

    private static RouteValueDictionary GetDefaults(RouteTemplate parsedTemplate)
    {
        var result = new RouteValueDictionary();

        foreach (var parameter in parsedTemplate.Parameters)
            if (parameter.DefaultValue != null)
                result.Add(parameter.Name!, parameter.DefaultValue);

        return result;
    }
}

/// <summary>
/// Thread-safe, process-shared <see cref="IRouteTable"/> for the integration fixture. Registered as a singleton
/// so every scope (the middleware's request scope, the startup task's scope, the index observer's fresh scope)
/// mutates and reads the same table — matching the production <c>RouteTable</c>, whose state lives in the shared
/// memory cache. Enumeration order is preserved so "first deterministic match wins" is honoured.
/// </summary>
internal sealed class MemoryCacheRouteTable : IRouteTable
{
    private readonly object _sync = new();
    private readonly List<HttpRouteData> _routes = new();

    public ValueTask Add(string route) => Add(new HttpRouteData(route));

    public ValueTask Add(HttpRouteData httpRouteData)
    {
        lock (_sync)
            _routes.Add(httpRouteData);
        return ValueTask.CompletedTask;
    }

    public ValueTask Remove(string route)
    {
        lock (_sync)
            _routes.RemoveAll(r => StringComparer.Ordinal.Equals(r.Route, route));
        return ValueTask.CompletedTask;
    }

    public async ValueTask AddRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Add(route);
    }

    public ValueTask Refresh(IEnumerable<string> routes) => Refresh(routes.Select(r => new HttpRouteData(r)));

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        lock (_sync)
        {
            _routes.Clear();
            _routes.AddRange(routes);
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask RemoveRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Remove(route);
    }

    public IEnumerator<HttpRouteData> GetEnumerator()
    {
        lock (_sync)
            return _routes.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
