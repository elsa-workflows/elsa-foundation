using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Serialization.Core;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Elsa.Http.Services;

/// <summary>
/// Reads application/json and text/json content type streams.
/// </summary>
internal sealed class JsonHttpContentParser : IHttpContentParser
{
    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool GetSupportsContentType(HttpResponseParserContext context) => context.ContentType.Contains("json", StringComparison.InvariantCultureIgnoreCase);

    /// <inheritdoc />
    public async Task<object> ReadAsync(HttpResponseParserContext context)
    {
        var content = context.Content;
        using var reader = new StreamReader(content, leaveOpen: true);
        var json = await reader.ReadToEndAsync(context.CancellationToken);
        var returnType = context.ReturnType;

        if (returnType is null || returnType == typeof(string))
            return json;

        // Dynamic (Any) payload: the canonical runtime representation is a mutable JsonNode.
        if (returnType == typeof(object) || typeof(JsonNode).IsAssignableFrom(returnType)) return JsonNode.Parse(json)!;
        using var document = JsonDocument.Parse(json);
        return JsonConversionContract.For(returnType).Read(document.RootElement, returnType, context.ReportJsonDiagnostic)!;
    }
}
