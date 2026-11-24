using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>
/// The rules for the header fields and query parameters an HTTP request carries in and a response carries out. A
/// request sits in instance state, and both travel in checkpoints under the store's content cap, so each collection is
/// bounded. Header names are stored in lower case, as HTTP/2 sends them, so a binding path reads them one way
/// (<c>Headers.x-tenant</c>); lookups in code ignore case. A repeated field's values are joined with ", ", a repeated
/// query parameter's with ",".
/// </summary>
public static class HttpFields
{
    /// <summary>The most fields one collection holds.</summary>
    public const int MaxFields = 100;

    /// <summary>The most UTF-8 bytes the names and values of one collection take together.</summary>
    public const int MaxBytes = 32 * 1_024;

    /// <summary>
    /// Request headers that carry credentials. They are not kept: instance state, journals and history are readable by
    /// operators, so a workflow does not receive them (the old core's standalone HTTP trigger drops the same set).
    /// </summary>
    public static readonly IReadOnlySet<string> Credentials =
        new HashSet<string>(["authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Response headers the edge owns: the content type comes from the response's own field.</summary>
    public static readonly IReadOnlySet<string> Reserved =
        new HashSet<string>(["content-type", "content-length", "transfer-encoding"], StringComparer.OrdinalIgnoreCase);

    internal static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The request headers as the model keeps them: lower-case names, credentials dropped, bounded.</summary>
    public static IReadOnlyDictionary<string, string> RequestHeaders(IEnumerable<KeyValuePair<string, string>>? fields) =>
        Headers(fields?.Where(field => !Credentials.Contains(field.Key)), "request");

    /// <summary>The response headers as the model sends them: lower-case names, no reserved name, bounded.</summary>
    public static IReadOnlyDictionary<string, string> ResponseHeaders(IEnumerable<KeyValuePair<string, string>>? fields)
    {
        var headers = Headers(fields, "response");
        foreach (var name in headers.Keys)
            if (Reserved.Contains(name)) throw new HttpFieldsException($"The response header '{name}' is set by the HTTP edge, not by the workflow.");
        return headers;
    }

    /// <summary>The query parameters as the model keeps them: names as sent, bounded.</summary>
    public static IReadOnlyDictionary<string, string> Query(IEnumerable<KeyValuePair<string, string>>? fields)
    {
        if (fields is null) return None;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrEmpty(name)) throw new HttpFieldsException("A query parameter has no name.");
            query[name] = query.TryGetValue(name, out var earlier) ? earlier + "," + value : value ?? "";
        }
        return Bounded(query, "query string");
    }

    /// <summary>
    /// Response headers from a workflow's input: a JSON object whose values are strings, numbers or booleans. Anything
    /// else is refused, as are names that are not HTTP tokens and values with control characters.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ResponseHeaders(JsonObject? input)
    {
        if (input is null) return None;
        var fields = new List<KeyValuePair<string, string>>(input.Count);
        foreach (var (name, value) in input)
        {
            var text = value is JsonValue scalar ? scalar.GetValueKind() switch
            {
                JsonValueKind.String => scalar.GetValue<string>(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => scalar.ToJsonString(),
                _ => null,
            } : null;
            if (text is null) throw new HttpFieldsException($"The response header '{name}' must be a string, number or boolean.");
            fields.Add(new(name, text));
        }
        return ResponseHeaders(fields);
    }

    private static IReadOnlyDictionary<string, string> Headers(IEnumerable<KeyValuePair<string, string>>? fields, string side)
    {
        if (fields is null) return None;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (raw, value) in fields)
        {
            if (!IsToken(raw)) throw new HttpFieldsException($"'{raw}' is not a valid {side} header name.");
            if (value is not null && value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
                throw new HttpFieldsException($"The {side} header '{raw}' has a line break or NUL in its value.");
            var name = raw.ToLowerInvariant();
            headers[name] = headers.TryGetValue(name, out var earlier) ? earlier + ", " + value : value ?? "";
        }
        return Bounded(headers, side + " headers");
    }

    private static IReadOnlyDictionary<string, string> Bounded(Dictionary<string, string> fields, string what)
    {
        if (fields.Count > MaxFields) throw new HttpFieldsException($"The {what} has {fields.Count} fields; at most {MaxFields} are kept.");
        var bytes = 0L;
        foreach (var (name, value) in fields) bytes += Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value);
        if (bytes > MaxBytes) throw new HttpFieldsException($"The {what} takes {bytes} bytes; at most {MaxBytes} are kept.");
        return fields.Count == 0 ? None : fields;
    }

    // RFC 9110 section 5.6.2: token = 1*tchar.
    private static bool IsToken(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var c in name)
            if (!(char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c))) return false;
        return true;
    }
}

/// <summary>A request's or response's headers or query string broke one of the <see cref="HttpFields"/> rules.</summary>
public sealed class HttpFieldsException(string message) : ArgumentException(message);
