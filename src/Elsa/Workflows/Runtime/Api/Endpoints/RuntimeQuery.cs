using Microsoft.AspNetCore.Http;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// The mapper's lenient query helpers, kept for the two endpoints that bind values the constructor
/// binder cannot: an unparseable value reads as absent, never as a zero or a failure.
/// </summary>
internal static class RuntimeQuery
{
    public static string? Value(HttpContext context, string name) =>
        context.Request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    public static int? Int(HttpContext context, string name) =>
        int.TryParse(Value(context, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static DateTimeOffset? Date(HttpContext context, string name) =>
        DateTimeOffset.TryParse(Value(context, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
}
