using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Elsa.Http.Services;

/// <inheritdoc />
internal sealed class RouteMatcher : IRouteMatcher
{
    /// <inheritdoc />
    public RouteValueDictionary? Match(string routeTemplate, string route) =>
        Match(Compile(routeTemplate), route);

    /// <inheritdoc />
    public RouteValueDictionary? Match(HttpRouteData routeData, string route)
    {
        // Reuse the matcher the route table precompiled at refresh (issue #592 item 6); parse on the fly only for a
        // bare, hand-built entry that never went through Refresh.
        var matcher = routeData.CompiledMatcher as TemplateMatcher ?? Compile(routeData.Route);
        return Match(matcher, route);
    }

    private static RouteValueDictionary? Match(TemplateMatcher matcher, string route)
    {
        var values = new RouteValueDictionary();
        return matcher.TryMatch(route, values) ? values : null;
    }

    /// <summary>
    /// Parses a route template into a reusable <see cref="TemplateMatcher"/> (with its default values). The route
    /// table calls this once per route at refresh so request-time matching is lookup + match, never parse.
    /// </summary>
    public static TemplateMatcher Compile(string routeTemplate)
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
