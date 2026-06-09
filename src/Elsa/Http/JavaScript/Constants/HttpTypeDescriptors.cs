using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Http.JavaScript.Constants
{
    internal static class HttpTypeDescriptors
    {
        internal static IEnumerable<JavaScriptTypeDescriptor> GetDescriptors() => defaultTypes;
        internal static IEnumerable<(Type type, string alias)> GetAliases() => defaultAliases;

        private static readonly IEnumerable<(Type type, string alias)> defaultAliases =
        [
            (typeof(IFormFile[]), $"{nameof(IFormFile)}[]"),
            (typeof(HttpFile[]), $"{nameof(HttpFile)}[]"),
            (typeof(Downloadable[]), $"{nameof(Downloadable)}[]")
        ];

        private static readonly IEnumerable<JavaScriptTypeDescriptor> defaultTypes =
        [
            new(typeof(HttpRouteData)),
            new(typeof(HttpRequest)),
            new(typeof(HttpResponse)),
            new(typeof(HttpResponseMessage)),
            new(typeof(HttpHeaders)),
            new(typeof(IFormFile)),
            new(typeof(HttpFile)),
            new(typeof(Downloadable)),
            new (typeof(IFormFile[])),
            new (typeof(HttpFile[])),
            new (typeof(Downloadable[]))
        ];
    }
}
