using System.Net;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>One canned HTTP response for <see cref="RecordingHttpMessageHandler"/>.</summary>
public sealed class CannedHttpResponse
{
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    public string Content { get; init; } = "{}";

    /// <summary>
    /// Optional exact response bytes. When set, <see cref="ContentBytes"/> wins over
    /// <see cref="Content"/>: the handler sends these bytes and does not UTF-8-encode
    /// <see cref="Content"/>.
    /// </summary>
    public byte[]? ContentBytes { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static CannedHttpResponse Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new() { Status = status, Content = json };

    public static CannedHttpResponse Json(
        string json,
        HttpStatusCode status,
        IReadOnlyDictionary<string, string> headers) =>
        new() { Status = status, Content = json, Headers = headers };

    public static CannedHttpResponse WithStatus(
        HttpStatusCode status,
        string content = "",
        IReadOnlyDictionary<string, string>? headers = null) =>
        new()
        {
            Status = status,
            Content = content,
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

    public static CannedHttpResponse Bytes(
        byte[] content,
        HttpStatusCode status = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new CannedHttpResponse
        {
            Status = status,
            ContentBytes = content,
            Content = "",
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}

/// <summary>A captured outbound request, including the body the handler read.</summary>
public sealed class RecordedHttpRequest
{
    public required HttpMethod Method { get; init; }

    public required Uri? RequestUri { get; init; }

    public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; init; }

    public string? Body { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
