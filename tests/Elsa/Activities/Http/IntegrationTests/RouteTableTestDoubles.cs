using System.Collections;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// <see cref="IRouteMatcher"/> over the real ASP.NET <see cref="TemplateParser"/>/<see cref="TemplateMatcher"/>
/// — a verbatim copy of the production <c>Elsa.Http.Services.RouteMatcher</c> (which is internal to Elsa.Http).
/// Matching semantics are therefore identical to production, including the precompiled-matcher overload the route
/// table populates (issue #592 item 6).
/// </summary>
internal sealed class TestRouteMatcher : IRouteMatcher
{
    public RouteValueDictionary? Match(string routeTemplate, string route) => Match(Compile(routeTemplate), route);

    public RouteValueDictionary? Match(HttpRouteData routeData, string route) =>
        Match(routeData.CompiledMatcher as TemplateMatcher ?? Compile(routeData.Route), route);

    private static RouteValueDictionary? Match(TemplateMatcher matcher, string route)
    {
        var values = new RouteValueDictionary();
        return matcher.TryMatch(route, values) ? values : null;
    }

    private static TemplateMatcher Compile(string routeTemplate)
    {
        var template = TemplateParser.Parse(routeTemplate);
        return new TemplateMatcher(template, GetDefaults(template));
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
/// Process-shared <see cref="IRouteTable"/> for the integration fixture that delegates to the <b>production</b>
/// <see cref="RouteTable"/> over a shared <see cref="IMemoryCache"/> singleton. Every scope (the middleware's
/// request scope, the startup task's scope, the index observer's fresh scope) reads and mutates the same table,
/// exactly like production. Using the real implementation guarantees the double cannot offer an ordering or
/// matching guarantee production lacks (issue #592 items 1 + 6).
/// </summary>
internal sealed class MemoryCacheRouteTable : IRouteTable
{
    private readonly RouteTable _inner = new(new MemoryCache(new MemoryCacheOptions()), NullLogger<RouteTable>.Instance);

    public ValueTask Add(string route) => _inner.Add(route);
    public ValueTask Add(HttpRouteData httpRouteData) => _inner.Add(httpRouteData);
    public ValueTask Remove(string route) => _inner.Remove(route);
    public ValueTask AddRange(IEnumerable<string> routes) => _inner.AddRange(routes);
    public ValueTask Refresh(IEnumerable<string> routes) => _inner.Refresh(routes);
    public ValueTask Refresh(IEnumerable<HttpRouteData> routes) => _inner.Refresh(routes);
    public ValueTask RemoveRange(IEnumerable<string> routes) => _inner.RemoveRange(routes);
    public IEnumerator<HttpRouteData> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
