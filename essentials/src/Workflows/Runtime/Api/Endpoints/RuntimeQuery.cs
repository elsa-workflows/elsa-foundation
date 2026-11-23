using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// Helpers for query values outside the binding constructor. Temporal parameters are also declared
/// as endpoint metadata so the shared API boundary rejects invalid supplied dates before dispatch.
/// </summary>
internal static class RuntimeQuery
{
    public static string? Value(HttpContext context, string name) =>
        context.Request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    public static int? Int(HttpContext context, string name) =>
        int.TryParse(Value(context, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static DateTimeOffset? Date(HttpContext context, string name)
    {
        var text = Value(context, name);
        return text is null ? null : (DateTimeOffset)JsonConversionContract.For(typeof(DateTimeOffset))
            .Read(JsonSerializer.SerializeToElement(text, JsonBaseline.Options), typeof(DateTimeOffset))!;
    }
}
