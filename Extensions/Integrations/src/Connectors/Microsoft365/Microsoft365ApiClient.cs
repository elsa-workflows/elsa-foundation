using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Integrations.Connectors.Microsoft365;

internal sealed class Microsoft365ApiClient(
    HttpClient http,
    ApplyMicrosoft365Connection applyBoundConnection,
    Uri graphBaseUri,
    Microsoft365RetryOptions? retry = null,
    Microsoft365UploadOptions? upload = null)
{
    private static readonly HashSet<int> ThrottledStatuses = [429, 503];

    private static readonly string[] MicrosoftUploadHosts =
    [
        "sharepoint.com",
        "sharepoint-df.com",
        "sharepointonline.com",
        "1drv.com",
        "onedrive.com"
    ];

    private readonly Microsoft365RetryOptions _retry = retry ?? Microsoft365RetryOptions.Default;
    private readonly Microsoft365UploadOptions _upload = upload ?? Microsoft365UploadOptions.Default;
    private readonly Uri _graphBaseUri = NormalizeBase(graphBaseUri);

    public async Task<JsonElement> GetJsonAsync(
        string relativePath,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope,
        bool allowNotFound = false)
    {
        return await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, GraphUri(relativePath)),
            applyConnection: true,
            connection,
            context,
            cancellationToken,
            requiredScope,
            allowNotFound);
    }

    public async Task<JsonElement> GetJsonAbsoluteAsync(
        Uri uri,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            applyConnection: true,
            connection,
            context,
            cancellationToken,
            requiredScope,
            allowNotFound: false);
    }

    public async Task<JsonElement> PostJsonAsync(
        string relativePath,
        JsonNode? body,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope)
    {
        return await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, GraphUri(relativePath));
                if (body is not null)
                    request.Content = JsonContent(body);
                return request;
            },
            applyConnection: true,
            connection,
            context,
            cancellationToken,
            requiredScope,
            allowNotFound: false);
    }

    public async Task PostNoContentAsync(
        string relativePath,
        JsonNode? body,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope)
    {
        await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, GraphUri(relativePath));
                if (body is not null)
                    request.Content = JsonContent(body);
                return request;
            },
            applyConnection: true,
            connection,
            context,
            cancellationToken,
            requiredScope,
            allowNotFound: false);
    }

    public async Task<JsonElement> PutBytesAsync(
        string relativePath,
        byte[] content,
        string contentType,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope,
        string? query = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, GraphUri(relativePath, query))
                {
                    Content = new ByteArrayContent(content)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue(contentType) }
                    }
                };
                return request;
            },
            applyConnection: true,
            connection,
            context,
            cancellationToken,
            requiredScope,
            allowNotFound: false);
    }

    public async Task<(byte[] Content, string? ContentType, string? FileName)> GetContentAsync(
        string relativePath,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope)
    {
        var (_, content, contentType, fileName) = await SendCoreAsync(
                () => new HttpRequestMessage(HttpMethod.Get, GraphUri(relativePath)),
                applyConnection: true,
                connection,
                context,
                cancellationToken,
                requiredScope,
                allowNotFound: false)
            .ConfigureAwait(false);
        return (content, contentType, fileName);
    }

    public async Task<IReadOnlyList<JsonElement>> CollectPagesAsync(
        string relativePath,
        int maxItems,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope)
    {
        if (maxItems < 1)
            maxItems = 1;

        var items = new List<JsonElement>();
        string? nextLink = null;
        var first = true;

        while (items.Count < maxItems)
        {
            JsonElement root;
            if (first)
            {
                root = await GetJsonAsync(relativePath, connection, context, cancellationToken, requiredScope)
                    .ConfigureAwait(false);
                first = false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(nextLink)
                    || !Uri.TryCreate(nextLink, UriKind.Absolute, out var nextUri)
                    || !IsAllowedGraphUri(nextUri))
                {
                    break;
                }

                root = await GetJsonAbsoluteAsync(nextUri, connection, context, cancellationToken, requiredScope)
                    .ConfigureAwait(false);
            }

            if (root.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    items.Add(element.Clone());
                    if (items.Count >= maxItems)
                        break;
                }
            }

            nextLink = root.TryGetProperty("@odata.nextLink", out var link) && link.ValueKind == JsonValueKind.String
                ? link.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(nextLink) || items.Count >= maxItems)
                break;
        }

        return items;
    }

    public async Task<JsonElement> UploadItemAsync(
        string itemPath,
        byte[] content,
        string? conflictBehavior,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentNullException.ThrowIfNull(content);

        var behavior = GraphPayloads.ConflictBehavior(conflictBehavior);

        if (content.Length <= _upload.SimpleUploadMaxBytes)
        {
            var query =
                $"{Uri.EscapeDataString("@microsoft.graph.conflictBehavior")}={Uri.EscapeDataString(behavior)}";
            return await PutBytesAsync(
                    $"{itemPath}/content",
                    content,
                    "application/octet-stream",
                    connection,
                    context,
                    cancellationToken,
                    requiredScope,
                    query)
                .ConfigureAwait(false);
        }

        var sessionBody = new JsonObject
        {
            ["item"] = new JsonObject
            {
                ["@microsoft.graph.conflictBehavior"] = behavior
            }
        };
        var session = await PostJsonAsync(
                $"{itemPath}/createUploadSession",
                sessionBody,
                connection,
                context,
                cancellationToken,
                requiredScope)
            .ConfigureAwait(false);

        var uploadUrl = session.TryGetProperty("uploadUrl", out var urlElement) ? urlElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(uploadUrl)
            || !Uri.TryCreate(uploadUrl, UriKind.Absolute, out var uploadUri)
            || !IsAllowedUploadUri(uploadUri))
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidResponse,
                "Microsoft Graph did not return a Microsoft upload URL.");
        }

        JsonElement last = default;
        var chunkSize = Math.Max(1, _upload.ChunkSizeBytes);
        for (var offset = 0; offset < content.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, content.Length - offset);
            var chunk = content.AsSpan(offset, length).ToArray();
            var (status, payload) = await PutChunkWithoutAuthAsync(
                    uploadUri,
                    chunk,
                    offset,
                    offset + length - 1,
                    content.Length,
                    cancellationToken)
                .ConfigureAwait(false);
            if ((int)status is < 200 or >= 300)
            {
                throw new Microsoft365ApiException(
                    Microsoft365ErrorCodes.HttpError,
                    $"Microsoft Graph upload host returned HTTP {(int)status}.");
            }

            last = TryParseJson(payload);
        }

        return last.ValueKind == JsonValueKind.Undefined ? default : last;
    }

    internal bool IsAllowedGraphUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.IdnHost, _graphBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedUploadUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.IdnHost;
        foreach (var suffix in MicrosoftUploadHosts)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<JsonElement> SendAsync(
        Func<HttpRequestMessage> createRequest,
        bool applyConnection,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope,
        bool allowNotFound)
    {
        var (status, content, _, _) = await SendCoreAsync(
                createRequest,
                applyConnection,
                connection,
                context,
                cancellationToken,
                requiredScope,
                allowNotFound)
            .ConfigureAwait(false);

        if (allowNotFound && status == HttpStatusCode.NotFound)
            return default;

        if (content.Length == 0)
            return default;

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidResponse,
                "Microsoft Graph returned a non-JSON body.");
        }
    }

    private async Task<(HttpStatusCode Status, byte[] Content, string? ContentType, string? FileName)> SendCoreAsync(
        Func<HttpRequestMessage> createRequest,
        bool applyConnection,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        string requiredScope,
        bool allowNotFound)
    {
        var attempts = Math.Max(1, _retry.MaxAttempts);
        var waited = TimeSpan.Zero;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dispatched = await SendOnceWithAuthRefreshAsync(
                    createRequest,
                    applyConnection,
                    connection,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);

            var code = (int)dispatched.Status;
            if (ShouldRetryStatus(code, dispatched.Method))
            {
                if (attempt >= attempts)
                {
                    throw new Microsoft365ApiException(
                        Microsoft365ErrorCodes.RateLimited,
                        "Microsoft Graph rate-limited the request after the retry budget was exhausted.",
                        isRetryable: true);
                }

                var delay = CapDelay(dispatched.RetryAfter ?? _retry.DefaultRetryAfter);
                if (waited + delay > _retry.MaxTotalWait)
                {
                    throw new Microsoft365ApiException(
                        Microsoft365ErrorCodes.RateLimited,
                        "Microsoft Graph rate-limited the request and the remaining wait would exceed the retry cap.",
                        isRetryable: true);
                }

                waited += delay;
                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (allowNotFound && dispatched.Status == HttpStatusCode.NotFound)
                return (dispatched.Status, dispatched.Content, dispatched.ContentType, dispatched.FileName);

            if (code is >= 200 and < 300)
                return (dispatched.Status, dispatched.Content, dispatched.ContentType, dispatched.FileName);

            ThrowGraphFault(dispatched.Status, dispatched.Content, connection, requiredScope);
        }

        throw new Microsoft365ApiException(
            Microsoft365ErrorCodes.RateLimited,
            "Microsoft Graph rate-limited the request after the retry budget was exhausted.",
            isRetryable: true);
    }

    private static bool ShouldRetryStatus(int status, HttpMethod method) =>
        ThrottledStatuses.Contains(status)
        || status == 504 && HttpMethod.Get.Equals(method);

    private async Task<DispatchedResponse> SendOnceWithAuthRefreshAsync(
        Func<HttpRequestMessage> createRequest,
        bool applyConnection,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken)
    {
        var first = await DispatchAsync(createRequest, applyConnection, refresh: false, connection, context, cancellationToken)
            .ConfigureAwait(false);

        if ((int)first.Status != 401 || !first.CanRefresh)
            return first;

        return await DispatchAsync(createRequest, applyConnection, refresh: true, connection, context, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DispatchedResponse> DispatchAsync(
        Func<HttpRequestMessage> createRequest,
        bool applyConnection,
        bool refresh,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var request = createRequest();
        if (refresh)
            request.Options.Set(HttpConnectionApplyOptions.RefreshCredentials, true);
        if (applyConnection)
            await applyBoundConnection(request, connection, context, cancellationToken).ConfigureAwait(false);

        var canRefresh = request.Options.TryGetValue(HttpConnectionApplyOptions.HasRefreshableState, out var refreshable)
            && refreshable;

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new DispatchedResponse(
            response.StatusCode,
            content,
            response.Content.Headers.ContentType?.ToString(),
            ReadFileName(response),
            ReadRetryAfter(response),
            canRefresh,
            request.Method);
    }

    private async Task<(HttpStatusCode Status, string Payload)> PutChunkWithoutAuthAsync(
        Uri uploadUrl,
        byte[] content,
        long start,
        long end,
        long total,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(content)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue("application/octet-stream"),
                    ContentRange = new ContentRangeHeaderValue(start, end, total),
                    ContentLength = content.Length
                }
            }
        };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, payload);
    }

    private void ThrowGraphFault(
        HttpStatusCode status,
        byte[] content,
        ConnectionReference? connection,
        string requiredScope)
    {
        var payload = Encoding.UTF8.GetString(content);
        var (code, message) = ReadGraphError(payload, (int)status);

        if ((int)status == 401 || IsConnectionUnavailable(code))
        {
            var name = string.IsNullOrWhiteSpace(connection?.Name) ? "http" : connection.Name.Trim();
            var reason = IsConnectionUnavailable(code) ? code : "401";
            throw new ConnectionUnavailableException(
                name,
                ConnectionFaultCodes.Unavailable,
                $"Microsoft 365 connection '{name}' is unavailable ({reason}). Re-authorize the connection.");
        }

        if ((int)status == 403 && IsAccessDenied(code))
        {
            throw new Microsoft365ApiException(
                code,
                $"Microsoft Graph denied access. Required scope: {requiredScope}.",
                isRetryable: false,
                needed: requiredScope);
        }

        var retryable = (int)status >= 500;
        throw new Microsoft365ApiException(code, message, retryable);
    }

    internal static (string Code, string Message) ReadGraphError(string payload, int status)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    var tokenError = Truncate(error.GetString()) ?? Microsoft365ErrorCodes.HttpError;
                    var tokenDescription = root.TryGetProperty("error_description", out var description)
                        && description.ValueKind == JsonValueKind.String
                        ? Truncate(description.GetString())
                        : null;
                    return (tokenError, tokenDescription ?? $"Microsoft Graph HTTP {status}.");
                }

                if (error.ValueKind == JsonValueKind.Object)
                {
                    var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                        ? Truncate(codeElement.GetString())
                        : null;
                    var message = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                        ? Truncate(messageElement.GetString())
                        : null;
                    return (
                        string.IsNullOrWhiteSpace(code) ? Microsoft365ErrorCodes.HttpError : code,
                        string.IsNullOrWhiteSpace(message) ? $"Microsoft Graph HTTP {status}." : message);
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON bodies are never copied into the fault.
        }

        return (Microsoft365ErrorCodes.HttpError, $"Microsoft Graph HTTP {status}.");
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (_retry.DelayAsync is not null)
        {
            await _retry.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (delay <= TimeSpan.Zero)
            return;

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan CapDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        return delay > _retry.MaxRetryAfter ? _retry.MaxRetryAfter : delay;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            return until < TimeSpan.Zero ? TimeSpan.Zero : until;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private Uri GraphUri(string relativePath, string? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var path = relativePath.TrimStart('/');
        var uri = new Uri($"{_graphBaseUri.ToString().TrimEnd('/')}/{Microsoft365Api.VersionSegment}/{path}", UriKind.Absolute);
        if (string.IsNullOrWhiteSpace(query))
            return uri;

        var suffix = query.TrimStart('?', '&');
        return new Uri($"{uri.AbsoluteUri}?{suffix}", UriKind.Absolute);
    }

    private static Uri NormalizeBase(Uri graphBaseUri)
    {
        ArgumentNullException.ThrowIfNull(graphBaseUri);
        if (!string.Equals(graphBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new Microsoft365ApiException(
                Microsoft365ErrorCodes.InvalidInput,
                "Graph base URL must be an https URL.");
        }

        return graphBaseUri;
    }

    private static StringContent JsonContent(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static JsonElement TryParseJson(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return default;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ReadFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        if (disposition is not null)
        {
            var name = disposition.FileNameStar ?? disposition.FileName;
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim('"');
        }

        if (response.Headers.TryGetValues("Content-Disposition", out var values))
        {
            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw)
                && ContentDispositionHeaderValue.TryParse(raw, out var parsed))
            {
                var name = parsed.FileNameStar ?? parsed.FileName;
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim('"');
            }
        }

        return null;
    }

    private static string? Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return text.Length <= Microsoft365Api.ErrorTextLimit ? text : text[..Microsoft365Api.ErrorTextLimit];
    }

    private static bool IsConnectionUnavailable(string code) =>
        string.Equals(code, Microsoft365ErrorCodes.InvalidAuthenticationToken, StringComparison.OrdinalIgnoreCase)
        || string.Equals(code, Microsoft365ErrorCodes.InvalidGrant, StringComparison.OrdinalIgnoreCase);

    private static bool IsAccessDenied(string code) =>
        string.Equals(code, Microsoft365ErrorCodes.AuthorizationRequestDenied, StringComparison.OrdinalIgnoreCase)
        || string.Equals(code, Microsoft365ErrorCodes.ErrorAccessDenied, StringComparison.OrdinalIgnoreCase);

    private readonly record struct DispatchedResponse(
        HttpStatusCode Status,
        byte[] Content,
        string? ContentType,
        string? FileName,
        TimeSpan? RetryAfter,
        bool CanRefresh,
        HttpMethod Method);
}

internal delegate ValueTask ApplyMicrosoft365Connection(
    HttpRequestMessage request,
    ConnectionReference? connection,
    ActivityExecutionContext context,
    CancellationToken cancellationToken);
