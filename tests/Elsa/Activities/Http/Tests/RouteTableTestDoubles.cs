using System.Collections;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Minimal list-backed <see cref="IRouteTable"/> for middleware unit scope. The middleware only enumerates the
/// table (via <see cref="IEnumerable{HttpRouteData}"/>) and reads each entry's <see cref="HttpRouteData.Route"/>,
/// so the mutation members are implemented straightforwardly for completeness; enumeration order is preserved so
/// "first deterministic match wins" is testable.
/// </summary>
internal sealed class FakeRouteTable : IRouteTable
{
    private readonly List<HttpRouteData> _routes = new();

    public FakeRouteTable(params string[] templates)
    {
        foreach (var template in templates)
            _routes.Add(new HttpRouteData(template));
    }

    public ValueTask Add(string route) => Add(new HttpRouteData(route));

    public ValueTask Add(HttpRouteData httpRouteData)
    {
        _routes.Add(httpRouteData);
        return ValueTask.CompletedTask;
    }

    public ValueTask Remove(string route)
    {
        _routes.RemoveAll(r => StringComparer.Ordinal.Equals(r.Route, route));
        return ValueTask.CompletedTask;
    }

    public async ValueTask AddRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Add(route);
    }

    public ValueTask Refresh(IEnumerable<string> routes)
    {
        _routes.Clear();
        return AddRange(routes);
    }

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        _routes.Clear();
        _routes.AddRange(routes);
        return ValueTask.CompletedTask;
    }

    public async ValueTask RemoveRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Remove(route);
    }

    public IEnumerator<HttpRouteData> GetEnumerator() => _routes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// <see cref="IRouteMatcher"/> over the real ASP.NET <see cref="TemplateParser"/>/<see cref="TemplateMatcher"/>
/// — a verbatim copy of the production <c>Elsa.Http.Services.RouteMatcher</c> (which is internal to Elsa.Http and
/// not referenced by this unit-test project). Matching semantics are therefore identical to production.
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
