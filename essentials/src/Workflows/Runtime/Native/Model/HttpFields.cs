using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>
/// The rules for the header fields and query parameters an HTTP request carries in and a response carries out. A
/// request sits in instance state, so each collection is bounded well below the store's content cap. Header names are
/// stored in lower case, as HTTP/2 sends them, so a binding path reads them one way (<c>Headers.x-tenant</c>), and a
/// lookup in code ignores their case. Query names are kept exactly as sent and compared with case, as a URL's query
/// is, so <c>Query.page</c> reads <c>?page=</c> and not <c>?Page=</c>. A repeated header's values are joined with ", ",
/// a repeated query parameter's with ",". A header value is visible ASCII, space or tab, as Kestrel accepts by default.
/// </summary>
/// <remarks>
/// The rules also run when a stored request or response is read back, so they may be loosened later, never tightened:
/// a tighter rule would refuse state written under the old one.
/// </remarks>
public static class HttpFields
{
    /// <summary>The most fields one collection holds.</summary>
    public const int MaxFields = 100;

    /// <summary>The most UTF-8 bytes the names and values of one collection take together, before JSON escaping.</summary>
    public const int MaxBytes = 32 * 1_024;

    /// <summary>
    /// Request headers that carry credentials. They are not kept: instance state, journals and history are readable by
    /// operators, so a workflow does not receive them (the old core's standalone HTTP trigger drops the same set).
    /// </summary>
    public static readonly FrozenSet<string> Credentials =
        new[] { "authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Response headers a workflow does not set: the content type is the response's own field, the framing and
    /// hop-by-hop headers belong to the edge, and <c>set-cookie</c> needs one field per cookie, which one value per name
    /// cannot carry.
    /// </summary>
    public static readonly FrozenSet<string> Reserved = new[]
    {
        "content-type", "content-length", "transfer-encoding", "connection", "keep-alive", "proxy-connection", "te", "trailer", "upgrade", "set-cookie",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> NoQuery = new Dictionary<string, string>().ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The request headers as the model keeps them: lower-case names, credentials dropped, bounded.</summary>
    public static IReadOnlyDictionary<string, string> RequestHeaders(IEnumerable<KeyValuePair<string, string>>? fields) =>
        Headers(fields?.Where(field => !Credentials.Contains(field.Key)), "request", joinRepeats: true);

    /// <summary>The response headers as the model sends them: lower-case names, one value each, no reserved name, bounded.</summary>
    public static IReadOnlyDictionary<string, string> ResponseHeaders(IEnumerable<KeyValuePair<string, string>>? fields)
    {
        var headers = Headers(fields, "response", joinRepeats: false);
        foreach (var name in headers.Keys)
            if (Reserved.Contains(name)) throw new HttpFieldsException($"The response header '{name}' cannot be set by a workflow.");
        return headers;
    }

    /// <summary>The query parameters as the model keeps them: names exactly as sent, bounded. A parameter without a name is left out.</summary>
    public static IReadOnlyDictionary<string, string> Query(IEnumerable<KeyValuePair<string, string>>? fields)
    {
        if (fields is null) return NoQuery;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrEmpty(name)) continue;
            query[name] = query.TryGetValue(name, out var earlier) ? earlier + "," + value : value ?? "";
        }
        return query.Count == 0 ? NoQuery : Bounded(query, "query string");
    }

    /// <summary>The route values as the model keeps them: names in lower case, as the template has them, values as decoded; bounded.</summary>
    public static IReadOnlyDictionary<string, string> RouteValues(IEnumerable<KeyValuePair<string, string>>? fields)
    {
        if (fields is null) return None;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in fields)
            if (!string.IsNullOrEmpty(name) && !values.TryAdd(name.ToLowerInvariant(), value ?? ""))
                throw new HttpFieldsException($"The route value '{name}' is given twice.");
        return Bounded(values, "route values");
    }

    /// <summary>A JSON object's scalar values as strings: route values or headers a workflow binds.</summary>
    internal static List<KeyValuePair<string, string>> Scalars(JsonObject input, string what)
    {
        var fields = new List<KeyValuePair<string, string>>(input.Count);
        foreach (var (name, value) in input)
        {
            var text = value is JsonValue scalar ? scalar.GetValueKind() switch
            {
                JsonValueKind.String => scalar.TryGetValue<string>(out var s) ? s : JsonSerializer.Deserialize<string>(scalar.ToJsonString()),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => scalar.ToJsonString(),
                _ => null,
            } : null;
            if (text is null) throw new HttpFieldsException($"The {what} '{name}' must be a string, number or boolean.");
            fields.Add(new(name, text));
        }
        return fields;
    }

    /// <summary>
    /// Response headers from a workflow's input: a JSON object whose values are strings, numbers or booleans. Anything
    /// else is refused, as are names that are not HTTP tokens, two names that differ only in case, and values outside
    /// visible ASCII, space and tab.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ResponseHeaders(JsonObject? input)
    {
        return input is null ? None : ResponseHeaders(Scalars(input, "response header"));
    }

    private static IReadOnlyDictionary<string, string> Headers(IEnumerable<KeyValuePair<string, string>>? fields, string side, bool joinRepeats)
    {
        if (fields is null) return None;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (raw, value) in fields)
        {
            if (!IsToken(raw)) throw new HttpFieldsException($"'{raw}' is not a valid {side} header name.");
            if (value is not null && !IsFieldValue(value))
                throw new HttpFieldsException($"The {side} header '{raw}' has a character other than visible ASCII, space or tab in its value.");
            var name = raw.ToLowerInvariant();
            if (headers.TryGetValue(name, out var earlier))
            {
                if (!joinRepeats) throw new HttpFieldsException($"The {side} header '{name}' is given twice.");
                headers[name] = earlier + ", " + value;
            }
            else headers[name] = value ?? "";
        }
        return Bounded(headers, side + " headers");
    }

    private static IReadOnlyDictionary<string, string> Bounded(Dictionary<string, string> fields, string what)
    {
        if (fields.Count > MaxFields) throw new HttpFieldsException($"The {what} has {fields.Count} fields; at most {MaxFields} are kept.", overBound: true);
        var bytes = 0L;
        foreach (var (name, value) in fields) bytes += Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value);
        if (bytes > MaxBytes) throw new HttpFieldsException($"The {what} takes {bytes} bytes; at most {MaxBytes} are kept.", overBound: true);
        return fields.Count == 0 ? None : new ReadOnlyDictionary<string, string>(fields);
    }

    // RFC 9110 section 5.6.2: token = 1*tchar.
    private static bool IsToken(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var c in name)
            if (!(char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c))) return false;
        return true;
    }

    // RFC 9110 section 5.5 without obs-text: visible ASCII, space and tab.
    private static bool IsFieldValue(string value)
    {
        foreach (var c in value)
            if (c is not ('\t' or (>= ' ' and <= '~'))) return false;
        return true;
    }
}

/// <summary>A request's or response's headers or query string broke one of the <see cref="HttpFields"/> rules.</summary>
public sealed class HttpFieldsException(string message, bool overBound = false) : ArgumentException(message)
{
    /// <summary>True when the collection was over <see cref="HttpFields.MaxFields"/> or <see cref="HttpFields.MaxBytes"/>, false when a name or value was not allowed.</summary>
    public bool OverBound { get; } = overBound;
}
