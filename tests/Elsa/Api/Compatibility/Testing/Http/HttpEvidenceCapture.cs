using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Serialization;

namespace Elsa.Api.Compatibility.Testing.Http;

/// <summary>Runs compatibility cases and captures only bounded, consumed protocol evidence.</summary>
public static class HttpEvidenceCapture
{
    public static Task<HttpCompatibilityObservation> CaptureAsync(HttpClient client, HttpCompatibilityCase testCase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(testCase);
        return CaptureCoreAsync(client, testCase, cancellationToken);
    }

    public static HttpCompatibilityObservation Capture(HttpClient client, HttpCompatibilityCase testCase) =>
        CaptureAsync(client, testCase).GetAwaiter().GetResult();

    private static async Task<HttpCompatibilityObservation> CaptureCoreAsync(HttpClient client,
        HttpCompatibilityCase testCase, CancellationToken cancellationToken)
    {
        using var request = testCase.CreateRequest();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        var (bytes, bounded) = await ReadResponseAsync(response, testCase, cancellationToken);
        var text = Encoding.UTF8.GetString(bytes);
        var json = IsJson(contentType) && TryCanonicalJson(text, out var canonical) ? canonical : "";
        var body = json.Length == 0 ? NormalizeText(text) : json;
        var problem = contentType.Contains("problem+json", StringComparison.OrdinalIgnoreCase) || response.StatusCode >= System.Net.HttpStatusCode.BadRequest
            ? json : "";
        var isStreaming = testCase.BoundedStreaming || contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase);
        var streaming = isStreaming
            ? CaptureFrames(text, testCase.BoundedStreaming ? testCase.MaxStreamFrames : int.MaxValue) : "";
        var binding = testCase.Binding ?? DescribeRequest(request);
        var paging = testCase.PagingFiltering ?? DescribePaging(request, response);
        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(x => x.Key.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => string.Join(",", x.SelectMany(h => h.Value).Order(StringComparer.Ordinal)), StringComparer.Ordinal);

        return new HttpCompatibilityObservation
        {
            Endpoint = testCase.Endpoint,
            Case = testCase.Case,
            Binding = binding,
            Json = json,
            StatusCode = (int)response.StatusCode,
            ContentType = contentType,
            Headers = new SortedDictionary<string, string>(headers, StringComparer.Ordinal),
            Body = body,
            ProblemDetails = problem,
            PagingFiltering = paging,
            Streaming = streaming,
            TerminalState = bounded ? "Bounded" : "Completed"
        };
    }

    private static async Task<(byte[] Bytes, bool Bounded)> ReadResponseAsync(HttpResponseMessage response, HttpCompatibilityCase testCase,
        CancellationToken cancellationToken)
    {
        if (!testCase.BoundedStreaming)
        {
            return (await response.Content.ReadAsByteArrayAsync(cancellationToken), false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[8192];
        using var output = new MemoryStream();
        var remaining = Math.Max(0, testCase.MaxStreamBytes);
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) break;
            output.Write(buffer, 0, read);
            remaining -= read;
        }
        var bounded = remaining == 0 && (response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength > output.Length);
        return (output.ToArray(), bounded);
    }

    private static string DescribeRequest(HttpRequestMessage request)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        var contentType = request.Content?.Headers.ContentType?.ToString() ?? "";
        return $"method={request.Method.Method};uri={uri};content-type={contentType}";
    }

    private static string DescribePaging(HttpRequestMessage request, HttpResponseMessage response)
    {
        var query = request.RequestUri?.Query ?? "";
        var links = response.Headers.TryGetValues("Link", out var values) ? string.Join(",", values.Order(StringComparer.Ordinal)) : "";
        return query.Contains("page", StringComparison.OrdinalIgnoreCase) || query.Contains("filter", StringComparison.OrdinalIgnoreCase)
            ? $"query={query};link={links}" : "";
    }

    private static string CaptureFrames(string text, int maxFrames) =>
        string.Join("\n", text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Take(Math.Max(0, maxFrames)).Select(NormalizeText));

    private static bool IsJson(string contentType) => contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static bool TryCanonicalJson(string text, out string canonical)
    {
        try { canonical = CompatibilityJson.Canonicalize(text); return true; }
        catch (JsonException) { canonical = ""; return false; }
        catch (InvalidDataException) { canonical = ""; return false; }
    }

    private static string NormalizeText(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
