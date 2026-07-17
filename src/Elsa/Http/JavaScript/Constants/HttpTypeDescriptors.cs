using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Http.JavaScript.Constants;

internal static class HttpTypeDescriptors
{
    internal static IEnumerable<Type> GetTypes() => defaultTypes;
    internal static IEnumerable<(Type type, string alias)> GetAliases() => defaultAliases;

    private static readonly IEnumerable<Type> defaultTypes =
    [
        typeof(HttpRouteData),
        typeof(HttpRequest),
        typeof(HttpResponse),
        typeof(HttpResponseMessage),
        typeof(HttpHeaders),
        typeof(IFormFile),
        typeof(HttpFile),
        typeof(Downloadable),
        typeof(IFormFile[]),
        typeof(HttpFile[]),
        typeof(Downloadable[])
    ];

    private static readonly IEnumerable<(Type type, string alias)> defaultAliases =
    [
        (typeof(IFormFile[]), $"{nameof(IFormFile)}[]"),
        (typeof(HttpFile[]), $"{nameof(HttpFile)}[]"),
        (typeof(Downloadable[]), $"{nameof(Downloadable)}[]")
    ];
}
