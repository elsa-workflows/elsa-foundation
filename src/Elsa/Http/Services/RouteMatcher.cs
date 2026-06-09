using Elsa.Http.Core.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Elsa.Http.Services
{
    /// <inheritdoc />
    internal sealed class RouteMatcher : IRouteMatcher
    {
        /// <inheritdoc />
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
}
