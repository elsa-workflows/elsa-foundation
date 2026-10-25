using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Integrations.Connectors.Slack;

internal sealed class SlackApiClient(
    HttpClient http,
    ApplySlackConnection applyBoundConnection,
    ILogger logger,
    SlackRetryOptions? retry = null)
{
    private readonly SlackRetryOptions _retry = retry ?? SlackRetryOptions.Default;

    public async Task<JsonElement> PostJsonAsync(
        string method,
        JsonObject body,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? allowedErrors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(body);

        var uri = new Uri(SlackApi.BaseUrl + method, UriKind.Absolute);
        return await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                return request;
            },
            applyConnection: true,
            connection,
            context,
            cancellationToken,
            allowedErrors);
    }

    public async Task<HttpStatusCode> PostBytesWithoutAuthAsync(
        Uri uploadUrl,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploadUrl);
        ArgumentNullException.ThrowIfNull(content);

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = new ByteArrayContent(content)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
            }
        };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.StatusCode;
    }

    public async Task<IReadOnlyList<JsonElement>> CollectPagesAsync(
        string method,
        JsonObject seed,
        string arrayProperty,
        int maxPages,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arrayProperty);
        if (maxPages < 1)
            maxPages = 1;

        var items = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < maxPages; page++)
        {
            var body = seed.DeepClone() as JsonObject ?? [];
            if (!string.IsNullOrEmpty(cursor))
                body["cursor"] = cursor;

            var root = await PostJsonAsync(method, body, connection, context, cancellationToken).ConfigureAwait(false);
            if (root.TryGetProperty(arrayProperty, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                    items.Add(element.Clone());
            }

            cursor = null;
            if (root.TryGetProperty("response_metadata", out var metadata)
                && metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty("next_cursor", out var next))
            {
                cursor = next.GetString();
            }

            if (string.IsNullOrEmpty(cursor))
                break;
        }

        return items;
    }

    private async Task<JsonElement> SendAsync(
        Func<HttpRequestMessage> createRequest,
        bool applyConnection,
        ConnectionReference? connection,
        ActivityExecutionContext context,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? allowedErrors)
    {
        var attempts = Math.Max(1, _retry.MaxAttempts);
        var waited = TimeSpan.Zero;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = createRequest();
            if (applyConnection)
                await applyBoundConnection(request, connection, context, cancellationToken).ConfigureAwait(false);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode == 429)
            {
                if (attempt >= attempts)
                {
                    throw new SlackApiException(
                        SlackErrorCodes.RateLimited,
                        "Slack rate-limited the request after the retry budget was exhausted.",
                        isRetryable: true);
                }

                var delay = ReadRetryAfter(response) ?? _retry.DefaultRetryAfter;
                if (delay > _retry.MaxRetryAfter)
                    delay = _retry.MaxRetryAfter;
                if (waited + delay > _retry.MaxTotalWait)
                {
                    throw new SlackApiException(
                        SlackErrorCodes.RateLimited,
                        "Slack rate-limited the request and the remaining wait would exceed the retry cap.",
                        isRetryable: true);
                }

                waited += delay;
                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new SlackApiException(
                    SlackErrorCodes.HttpError,
                    $"Slack HTTP {(int)response.StatusCode} on {request.RequestUri?.AbsolutePath}.");
            }

            return ReadEnvelope(payload, connection, allowedErrors);
        }

        throw new SlackApiException(
            SlackErrorCodes.RateLimited,
            "Slack rate-limited the request after the retry budget was exhausted.",
            isRetryable: true);
    }

    private JsonElement ReadEnvelope(string payload, ConnectionReference? connection, IReadOnlySet<string>? allowedErrors)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new SlackApiException(
                SlackErrorCodes.InvalidResponse,
                "Slack returned a non-JSON body.",
                isRetryable: false)
            {
                Data = { ["parse"] = exception.Message }
            };
        }
        if (root.TryGetProperty("warning", out var warning) && warning.ValueKind == JsonValueKind.String)
        {
            var text = warning.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                logger.LogWarning("Slack API warning: {Warning}", text);
        }

        var ok = !root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.False;
        if (ok)
            return root;

        var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : SlackErrorCodes.InvalidResponse;
        if (string.IsNullOrWhiteSpace(error))
            error = SlackErrorCodes.InvalidResponse;

        if (IsConnectionUnavailable(error))
        {
            var name = string.IsNullOrWhiteSpace(connection?.Name) ? "http" : connection.Name.Trim();
            throw new ConnectionUnavailableException(
                name,
                ConnectionFaultCodes.Unavailable,
                $"Slack connection '{name}' is unavailable ({error}). Re-authorize the connection.");
        }

        if (allowedErrors is not null && allowedErrors.Contains(error))
            return root;

        var needed = root.TryGetProperty("needed", out var neededElement) && neededElement.ValueKind == JsonValueKind.String
            ? neededElement.GetString()
            : null;
        var message = string.Equals(error, SlackErrorCodes.MissingScope, StringComparison.Ordinal)
            ? string.IsNullOrWhiteSpace(needed)
                ? "Slack API error missing_scope."
                : $"Slack API error missing_scope. Needed scope: {needed}."
            : $"Slack API error {error}.";

        throw new SlackApiException(error, message, isRetryable: false, needed);
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

    private static bool IsConnectionUnavailable(string error) =>
        error is SlackErrorCodes.InvalidAuth or SlackErrorCodes.TokenRevoked or SlackErrorCodes.AccountInactive;
}

internal delegate ValueTask ApplySlackConnection(
    HttpRequestMessage request,
    ConnectionReference? connection,
    ActivityExecutionContext context,
    CancellationToken cancellationToken);
