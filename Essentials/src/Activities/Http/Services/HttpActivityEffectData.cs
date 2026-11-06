using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Activities.Http.Services;

/// <summary>HTTP-owned profile validation and safe interaction digests; never stores authored request values.</summary>
internal static class HttpActivityEffectData
{
    internal const string EffectKey = "elsa.http-request/1";
    internal const string SubstituteProfile = "elsa.http-response-substitute/1";
    internal const string TestSystemProfile = "elsa.http-test-system/1";

    internal static string Fingerprint(HttpActivityRequest request) => Digest(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", EffectKey);
        writer.WriteString("url", request.Url.OriginalString);
        writer.WriteString("method", request.Method);
        writer.WriteString("content", request.Content);
        writer.WriteString("contentType", request.ContentType);
        if (request.Timeout is { } timeout) writer.WriteNumber("timeoutTicks", timeout.Ticks); else writer.WriteNull("timeoutTicks");
        writer.WriteStartObject("headers");
        foreach (var pair in request.RequestHeaders.OrderBy(x => x.Key, StringComparer.Ordinal)) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
        writer.WritePropertyName("connection");
        if (request.Connection is null) writer.WriteNullValue();
        else
        {
            writer.WriteStartObject();
            writer.WriteString("name", request.Connection.Name);
            writer.WriteString("type", request.Connection.Type);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    });

    internal static string Fingerprint(SendHttpRequestResult result) => Digest(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", SubstituteProfile);
        if (result.StatusCode is { } status) writer.WriteNumber("statusCode", status); else writer.WriteNull("statusCode");
        writer.WriteString("body", result.ResponseBody);
        writer.WriteStartObject("headers");
        foreach (var pair in (result.ResponseHeaders ?? new Dictionary<string, string[]>()).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            writer.WriteStartArray(pair.Key);
            foreach (var value in pair.Value) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    internal static SendHttpRequestResult ReadSubstitute(JsonElement configuration)
    {
        var fields = ReadObject(configuration, ["statusCode", "body", "headers"]);
        if (!fields.TryGetValue("statusCode", out var status) || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code) || code is < 100 or > 599)
            throw Refuse("The HTTP substitute requires a valid response status.");
        string? body = null;
        if (fields.TryGetValue("body", out var bodyValue))
        {
            if (bodyValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw Refuse("The HTTP substitute body must be a string or null.");
            body = bodyValue.ValueKind == JsonValueKind.Null ? null : bodyValue.GetString();
        }
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (fields.TryGetValue("headers", out var headerValue))
        {
            if (headerValue.ValueKind != JsonValueKind.Object) throw Refuse("The HTTP substitute headers must be an object.");
            foreach (var header in headerValue.EnumerateObject())
            {
                if (!IsHeaderName(header.Name) || headers.ContainsKey(header.Name) || header.Value.ValueKind != JsonValueKind.Array)
                    throw Refuse("The HTTP substitute contains an invalid or duplicate header.");
                var values = new List<string>();
                foreach (var value in header.Value.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.AsSpan().IndexOfAny('\r', '\n') >= 0)
                        throw Refuse("The HTTP substitute contains an invalid header value.");
                    values.Add(text);
                }
                headers.Add(header.Name, values.ToArray());
            }
        }
        return new SendHttpRequestResult(code, body, headers);
    }

    internal static HttpActivityRequest ReadTestSystem(JsonElement configuration, HttpActivityRequest original)
    {
        var fields = ReadObject(configuration, ["url", "connection", "headers"]);
        if (!fields.TryGetValue("url", out var url) || url.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var destination) ||
            destination.Scheme is not ("http" or "https") || destination.UserInfo.Length != 0 || destination.Fragment.Length != 0)
            throw Refuse("The HTTP test-system mapping requires an explicit absolute HTTP destination without user information or a fragment.");
        if (!fields.TryGetValue("connection", out var reference)) throw Refuse("The HTTP test-system mapping requires an explicit connection.");
        var connection = ReadObject(reference, ["name", "type"]);
        if (!connection.TryGetValue("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
            throw Refuse("The HTTP test-system mapping requires a named connection.");
        if (connection.TryGetValue("type", out var type) &&
            (type.ValueKind != JsonValueKind.String || !string.Equals(type.GetString(), "http", StringComparison.OrdinalIgnoreCase)))
            throw Refuse("The HTTP test-system connection must have the HTTP type.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fields.TryGetValue("headers", out var configuredHeaders))
        {
            if (configuredHeaders.ValueKind != JsonValueKind.Object) throw Refuse("The HTTP test-system headers must be an object.");
            foreach (var header in configuredHeaders.EnumerateObject())
            {
                // These values come only from the admitted mapping. Original activity headers, including arbitrary
                // credential-bearing custom fields, are never copied. The selected connection owns authentication.
                if (!new[] { "Accept", "Accept-Language", "User-Agent" }.Contains(header.Name, StringComparer.OrdinalIgnoreCase) ||
                    header.Value.ValueKind != JsonValueKind.String || header.Value.GetString() is not { } value ||
                    value.AsSpan().IndexOfAny('\r', '\n') >= 0 || !headers.TryAdd(header.Name, value))
                    throw Refuse("The HTTP test-system mapping contains an unsupported, invalid or duplicate header.");
            }
        }
        return new HttpActivityRequest(destination, original.Method, original.Content, original.ContentType,
            headers, new ConnectionReference(name.GetString()!, "http"), original.Timeout);
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement element, IReadOnlyCollection<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Refuse("The HTTP effect configuration must be an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value))
                throw Refuse("The HTTP effect configuration contains an unknown or duplicate member.");
        return fields;
    }

    private static bool IsHeaderName(string name) => name.Length != 0 && name.All(character =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "!#$%&'*+-.^_`|~".Contains(character));

    private static string Digest(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    internal static RuntimeTestEffectUnavailableException Refuse(string message) => new("http-test-effect-unavailable", message);
}
