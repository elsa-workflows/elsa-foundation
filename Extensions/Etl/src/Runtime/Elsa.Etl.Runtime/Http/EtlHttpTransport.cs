using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Runtime.Http;

/// <summary>A bounded raw response returned by one HTTP source attempt.</summary>
/// <remarks>Its textual representation intentionally excludes the retained body.</remarks>
internal sealed record EtlHttpFetchResponse(ReadOnlyMemory<byte>? Body, DateTimeOffset? ResponseReceivedAtUtc,
    EtlHttpSourceFetchOutcome Outcome)
{
    public override string ToString() => nameof(EtlHttpFetchResponse);
}

/// <summary>Per-attempt ETL HTTP transport with no credential-bearing handler state.</summary>
/// <remarks>
/// The handler never follows redirects or stores cookies. Credentials are applied to one request message and
/// are never cached in a pooled handler. The client has no logger factory, and failures returned by this type
/// contain no request, response, URL, header, body, or provider exception details.
/// </remarks>
internal sealed class EtlHttpTransport : IDisposable
{
    private const int MaximumUriLength = 128 * 1024;
    private const int MaximumQueryParameters = 4096;
    private const int MaximumAuthenticationQueryParameters = 16;
    private const int MaximumAuthenticationQueryPartLength = 8192;
    private const int MaximumAuthenticationHeaderCount = 16;
    private const int MaximumAuthenticationHeaderValues = 8;
    private const int MaximumAuthenticationHeaderValueLength = 8192;
    private const int MaximumAuthenticationHeaderBytes = 32 * 1024;
    private const int BufferSize = 80 * 1024;
    private readonly TimeProvider _clock;
    private readonly HttpClient _client;
    private int _disposed;

    public EtlHttpTransport(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = Timeout.InfiniteTimeSpan,
            MaxResponseHeadersLength = 64,
            MaxResponseDrainSize = 0
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Sends one pinned source GET and retains a complete bounded 200 body only.</summary>
    public async ValueTask<EtlHttpFetchResponse> FetchAsync(HostHttpResolvedBinding binding, Uri requestUri,
        EtlHttpSourceFetchAttempt attempt, long maxBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateFetchArguments(binding, requestUri, attempt, maxBytes);
        var now = _clock.GetUtcNow();
        if (now < attempt.StartedAt)
            throw InvalidRequest();
        if (now >= attempt.ExpiresAt)
            return UncertainFetch();

        using var timeout = new CancellationTokenSource(attempt.ExpiresAt - now, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            ValidateRequestUri(binding, requestUri);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await ApplyAuthenticatorAsync(binding, request, expectedMethod: HttpMethod.Get, expectedContent: null,
                expectedBody: null, expectedUri: requestUri, expectedIdempotencyValues: null, linked.Token);

            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
                return UncertainFetch();

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var headersReceivedAt = _clock.GetUtcNow();
            if (timeout.IsCancellationRequested || headersReceivedAt >= attempt.ExpiresAt)
                return UncertainFetch();

            var status = (int)response.StatusCode;
            var retryAfter = ReadRetryAfter(response, headersReceivedAt);
            if (status != (int)HttpStatusCode.OK)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(null, null, new EtlHttpSourceFetchOutcome(status, retryAfter, InvalidPage: false));
            }

            if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maxBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested)
                    return UncertainFetch();
                return new(null, null, new EtlHttpSourceFetchOutcome(status, retryAfter, InvalidPage: true));
            }

            var bounded = await ReadBoundedBodyAsync(response.Content, maxBytes, linked.Token);
            if (bounded.TooLarge)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested)
                    return UncertainFetch();
                return new(null, null, new EtlHttpSourceFetchOutcome(status, retryAfter, InvalidPage: true));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var receivedAt = _clock.GetUtcNow();
            if (timeout.IsCancellationRequested || receivedAt >= attempt.ExpiresAt)
                return UncertainFetch();

            return new(new ReadOnlyMemory<byte>(bounded.Body!), receivedAt,
                new EtlHttpSourceFetchOutcome(status, retryAfter, InvalidPage: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return UncertainFetch();
        }
        catch (Exception exception) when (IsTransportUncertainty(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UncertainFetch();
        }
        catch (EtlBindingException)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }
    }

    /// <summary>Sends one pinned JSON POST with its retained effect identity as the idempotency header.</summary>
    public async ValueTask<EtlHttpDeliveryResponse> DeliverAsync(HostHttpResolvedBinding binding,
        EtlHttpDeliveryAttempt attempt, ReadOnlyMemory<byte> body, long maxResponseBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateDeliveryArguments(binding, attempt, body, maxResponseBytes);
        var requestUri = binding.EndpointPolicy.Endpoint;
        var now = _clock.GetUtcNow();
        if (now < attempt.StartedAt)
            throw InvalidRequest();
        if (now >= attempt.ExpiresAt)
            return UncertainDelivery();

        using var timeout = new CancellationTokenSource(attempt.ExpiresAt - now, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            ValidateRequestUri(binding, requestUri);
            var bodyBytes = body.ToArray();
            using var content = new ByteArrayContent(bodyBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
            if (!request.Headers.TryAddWithoutValidation(binding.IdempotencyHeader, attempt.EffectId))
                throw InvalidRequest();
            var idempotencyValues = request.Headers.GetValues(binding.IdempotencyHeader).ToArray();

            await ApplyAuthenticatorAsync(binding, request, expectedMethod: HttpMethod.Post, expectedContent: content,
                expectedBody: bodyBytes, expectedUri: requestUri, expectedIdempotencyValues: idempotencyValues, linked.Token);

            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
                return UncertainDelivery();

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var headersReceivedAt = _clock.GetUtcNow();
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested || headersReceivedAt >= attempt.ExpiresAt)
                return UncertainDelivery();

            var status = (int)response.StatusCode;
            var retryAfter = ReadRetryAfter(response, headersReceivedAt);
            await DrainBoundedAsync(response.Content, maxResponseBytes, linked.Token);
            cancellationToken.ThrowIfCancellationRequested();

            var completedAt = _clock.GetUtcNow();
            return timeout.IsCancellationRequested || completedAt >= attempt.ExpiresAt
                ? UncertainDelivery()
                : new EtlHttpDeliveryResponse(status, retryAfter) { ResponseReceivedAtUtc = completedAt };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return UncertainDelivery();
        }
        catch (Exception exception) when (IsTransportUncertainty(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UncertainDelivery();
        }
        catch (EtlBindingException)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _client.Dispose();
    }

    private async ValueTask ApplyAuthenticatorAsync(HostHttpResolvedBinding binding, HttpRequestMessage request,
        HttpMethod expectedMethod, HttpContent? expectedContent, ReadOnlyMemory<byte>? expectedBody, Uri expectedUri,
        IReadOnlyList<string>? expectedIdempotencyValues, CancellationToken cancellationToken)
    {
        var expectedVersion = request.Version;
        var expectedVersionPolicy = request.VersionPolicy;
        var beforeHeaders = SnapshotHeaders(request.Headers);
        var beforeContentHeaders = request.Content is null ? null : SnapshotHeaders(request.Content.Headers);
        var expectedAuthHeader = ExpectedAuthenticationHeader(binding.Connection);
        var expectedQueryName = ExpectedAuthenticationQueryName(binding.Connection);
        var customScheme = binding.Connection is not null && expectedAuthHeader is null && expectedQueryName is null &&
                           !IsBuiltInAuthScheme(binding.Connection.AuthScheme);
        if (expectedAuthHeader is not null &&
            (!EtlHttpHeaderPolicy.ValidHeaderName(expectedAuthHeader) || EtlHttpHeaderPolicy.IsTransportReservedHeader(expectedAuthHeader) ||
             expectedAuthHeader.Equals(binding.IdempotencyHeader, StringComparison.OrdinalIgnoreCase)))
            throw Unavailable();

        if (binding.Connection is null)
        {
            if (binding.Authenticator is not null)
                throw Unavailable();
            return;
        }

        if (binding.Authenticator is null ||
            !string.Equals(binding.Authenticator.SchemeName, binding.Connection.AuthScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(binding.ConnectionTypeName, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase))
            throw Unavailable();

        try
        {
            ValidateConnectionUri(binding.Connection, expectedUri);
            await binding.Authenticator.ApplyAsync(request, binding.Connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }

        if (request.Method != expectedMethod || !ReferenceEquals(request.Content, expectedContent) || request.RequestUri is null ||
            !request.RequestUri.IsAbsoluteUri || request.Headers.Host is not null || request.Version != expectedVersion ||
            request.VersionPolicy != expectedVersionPolicy)
            throw Unavailable();

        try
        {
            binding.EndpointPolicy.Validate(request.RequestUri);
            if (!SameEndpointPath(expectedUri, request.RequestUri))
                throw Unavailable();
            ValidateAuthenticationQuery(expectedUri, request.RequestUri, expectedQueryName, customScheme);
            if (binding.Connection is not null)
                ValidateConnectionUri(binding.Connection, request.RequestUri);
            ValidateHeaderMutations(beforeHeaders, request.Headers, expectedAuthHeader, binding.IdempotencyHeader,
                expectedIdempotencyValues, customScheme);
            if (!SameHeaders(beforeContentHeaders, request.Content is null ? null : SnapshotHeaders(request.Content.Headers)))
                throw Unavailable();
            if (request.Content is not null &&
                !string.Equals(request.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                throw Unavailable();
            if (expectedBody is { } body)
            {
                var actualBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!actualBody.AsSpan().SequenceEqual(body.Span))
                    throw Unavailable();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EtlBindingException)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }
    }

    private static void ValidateFetchArguments(HostHttpResolvedBinding? binding, Uri? requestUri,
        EtlHttpSourceFetchAttempt? attempt, long maxBytes)
    {
        if (binding is null || requestUri is null || attempt is null || binding.Role != "Source" ||
            !ValidAttemptId(attempt.RequestId) || attempt.Number <= 0 || attempt.Fence <= 0 ||
            !IsLowerSha256(attempt.RequestHash) || !ValidUtc(attempt.StartedAt) || !ValidUtc(attempt.ExpiresAt) ||
            attempt.ExpiresAt < attempt.StartedAt || maxBytes <= 0 || maxBytes > int.MaxValue ||
            !requestUri.IsAbsoluteUri || requestUri.OriginalString.Length > MaximumUriLength)
            throw InvalidRequest();
    }

    private static void ValidateDeliveryArguments(HostHttpResolvedBinding? binding, EtlHttpDeliveryAttempt? attempt,
        ReadOnlyMemory<byte> body, long maxResponseBytes)
    {
        if (binding is null || attempt is null || binding.Role is not ("MainOutput" or "ErrorOutput") ||
            !ValidAttemptId(attempt.RequestId) || attempt.Number <= 0 || attempt.Fence <= 0 ||
            !IsLowerSha256(attempt.EffectId) || !ValidUtc(attempt.StartedAt) || !ValidUtc(attempt.ExpiresAt) ||
            attempt.ExpiresAt < attempt.StartedAt || body.IsEmpty ||
            maxResponseBytes < 0 || maxResponseBytes > int.MaxValue ||
            !EtlHttpHeaderPolicy.ValidHeaderName(binding.IdempotencyHeader) || EtlHttpHeaderPolicy.IsReservedHeader(binding.IdempotencyHeader))
            throw InvalidRequest();
    }

    private static void ValidateRequestUri(HostHttpResolvedBinding binding, Uri uri)
    {
        try
        {
            if (uri.OriginalString.Length > MaximumUriLength)
                throw Unavailable();
            binding.EndpointPolicy.Validate(uri);
            if (binding.Connection is not null)
                ValidateConnectionUri(binding.Connection, uri);
            else if (binding.Authenticator is not null)
                throw Unavailable();
        }
        catch (EtlBindingException)
        {
            throw;
        }
        catch
        {
            throw Unavailable();
        }
    }

    private static void ValidateConnectionUri(IResolvedConnection connection, Uri uri)
    {
        if (connection.Hosts is null || !connection.Hosts.Allows(uri) ||
            uri.Scheme == Uri.UriSchemeHttp &&
            (!TryReadSetting(connection.Settings, HttpConnectionSettingKeys.AllowInsecureHttp, out var allowHttp) ||
             !bool.TryParse(allowHttp, out var allowed) || !allowed))
            throw Unavailable();
    }

    private static string? ExpectedAuthenticationHeader(IResolvedConnection? connection)
    {
        if (connection is null)
            return null;
        if (connection.AuthScheme.Equals(HttpAuthSchemes.Basic, StringComparison.OrdinalIgnoreCase) ||
            connection.AuthScheme.Equals(HttpAuthSchemes.Bearer, StringComparison.OrdinalIgnoreCase))
            return "Authorization";
        if (!connection.AuthScheme.Equals(HttpAuthSchemes.ApiKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ReadSetting(connection.Settings, HttpConnectionSettingKeys.ApiKeyPlacement, ApiKeyPlacements.Header),
                ApiKeyPlacements.Query, StringComparison.OrdinalIgnoreCase))
            return null;
        return ReadSetting(connection.Settings, HttpConnectionSettingKeys.ApiKeyHeaderName, "Authorization");
    }

    private static string? ExpectedAuthenticationQueryName(IResolvedConnection? connection)
    {
        if (connection is null || !connection.AuthScheme.Equals(HttpAuthSchemes.ApiKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ReadSetting(connection.Settings, HttpConnectionSettingKeys.ApiKeyPlacement, ApiKeyPlacements.Header),
                ApiKeyPlacements.Query, StringComparison.OrdinalIgnoreCase))
            return null;
        return ReadSetting(connection.Settings, HttpConnectionSettingKeys.ApiKeyQueryName, "api_key");
    }

    private static bool IsBuiltInAuthScheme(string scheme) =>
        scheme.Equals(HttpAuthSchemes.ApiKey, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(HttpAuthSchemes.Basic, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(HttpAuthSchemes.Bearer, StringComparison.OrdinalIgnoreCase);

    private static void ValidateHeaderMutations(IReadOnlyDictionary<string, string[]> before,
        HttpRequestHeaders afterHeaders, string? expectedAuthHeader, string idempotencyHeader,
        IReadOnlyList<string>? expectedIdempotencyValues, bool allowCustomAuthentication)
    {
        var after = SnapshotHeaders(afterHeaders);
        foreach (var pair in before)
        {
            if (pair.Key.Equals(expectedAuthHeader, StringComparison.OrdinalIgnoreCase))
                throw Unavailable();
            if (!after.TryGetValue(pair.Key, out var values) || !pair.Value.SequenceEqual(values, StringComparer.Ordinal))
                throw Unavailable();
        }

        var added = after.Keys.Where(key => !before.ContainsKey(key)).ToArray();
        if (expectedAuthHeader is not null)
        {
            if (added.Length != 1 || !added[0].Equals(expectedAuthHeader, StringComparison.OrdinalIgnoreCase) ||
                !after.TryGetValue(expectedAuthHeader, out var authValues) || authValues.Length != 1 ||
                !ValidAuthenticationHeaderValues(authValues))
                throw Unavailable();
        }
        else if (!allowCustomAuthentication && added.Length != 0)
            throw Unavailable();
        else if (allowCustomAuthentication && (added.Length > MaximumAuthenticationHeaderCount ||
                 added.Any(name => !EtlHttpHeaderPolicy.ValidHeaderName(name) || EtlHttpHeaderPolicy.IsTransportReservedHeader(name) ||
                     name.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals(idempotencyHeader, StringComparison.OrdinalIgnoreCase)) ||
                 added.Any(name => !after.TryGetValue(name, out var values) || !ValidAuthenticationHeaderValues(values)) ||
                 added.Sum(name => after[name].Sum(value => value.Length)) > MaximumAuthenticationHeaderBytes))
            throw Unavailable();

        if (expectedIdempotencyValues is not null &&
            (!after.TryGetValue(idempotencyHeader, out var idempotencyValues) ||
             !expectedIdempotencyValues.SequenceEqual(idempotencyValues, StringComparer.Ordinal)))
            throw Unavailable();
    }

    private static bool ValidAuthenticationHeaderValues(IReadOnlyList<string> values) =>
        values.Count is > 0 and <= MaximumAuthenticationHeaderValues &&
        values.All(value => value.Length <= MaximumAuthenticationHeaderValueLength &&
                            !value.Any(character => char.IsControl(character) && character != '\t'));

    private static Dictionary<string, string[]> SnapshotHeaders(HttpHeaders headers) => headers
        .ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    private static bool SameHeaders(IReadOnlyDictionary<string, string[]>? left,
        IReadOnlyDictionary<string, string[]>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var values) &&
            pair.Value.SequenceEqual(values, StringComparer.Ordinal));
    }

    private static void ValidateAuthenticationQuery(Uri beforeUri, Uri afterUri, string? expectedQueryName,
        bool allowCustomAuthentication)
    {
        var before = ReadQuery(beforeUri);
        var after = ReadQuery(afterUri);
        if (after.Count < before.Count || before.Where((item, index) => item != after[index]).Any())
            throw Unavailable();

        var additions = after.Skip(before.Count).ToArray();
        if (expectedQueryName is not null)
        {
            if (additions.Length != 1 || !additions[0].Name.Equals(expectedQueryName, StringComparison.OrdinalIgnoreCase) ||
                before.Any(item => item.Name.Equals(expectedQueryName, StringComparison.OrdinalIgnoreCase)))
                throw Unavailable();
            return;
        }

        // Unauthenticated requests and built-in header authenticators cannot change query state. Custom registered
        // schemes may add bounded query credentials/signature fields, but they cannot replace or shadow authored keys.
        if (additions.Length == 0)
            return;
        if (!allowCustomAuthentication)
            throw Unavailable();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in additions)
            if (item.Name.Length is 0 or > MaximumAuthenticationQueryPartLength ||
                item.Value.Length > MaximumAuthenticationQueryPartLength || item.Name.Any(char.IsControl) ||
                item.Value.Any(char.IsControl) || before.Any(original => original.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) ||
                !names.Add(item.Name))
                throw Unavailable();
        if (additions.Length > MaximumAuthenticationQueryParameters)
            throw Unavailable();
    }

    private static List<QueryParameter> ReadQuery(Uri uri)
    {
        var query = uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
        if (query.Length == 0)
            return [];
        var segments = query.Split('&');
        if (segments.Length > MaximumQueryParameters)
            throw Unavailable();
        var parameters = new List<QueryParameter>(segments.Length);
        foreach (var segment in segments)
        {
            var equals = segment.IndexOf('=');
            var name = equals < 0 ? segment : segment[..equals];
            var value = equals < 0 ? string.Empty : segment[(equals + 1)..];
            parameters.Add(new QueryParameter(DecodeQueryPart(name), DecodeQueryPart(value)));
        }
        return parameters;
    }

    private static string DecodeQueryPart(string value) => Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));

    private static bool SameEndpointPath(Uri before, Uri after) =>
        string.Equals(before.Scheme, after.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(before.IdnHost, after.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        before.Port == after.Port && string.Equals(before.AbsolutePath, after.AbsolutePath, StringComparison.Ordinal);

    private static async ValueTask<BoundedBody> ReadBoundedBodyAsync(HttpContent content, long maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && length > maxBytes)
            return new(null, TooLarge: true);

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            if (body.Length > maxBytes - read)
                return new(null, TooLarge: true);
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return new(body.ToArray(), TooLarge: false);
    }

    private static async ValueTask DrainBoundedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes == 0 || content.Headers.ContentLength is { } length && length > maxBytes)
            return;

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[BufferSize];
        long received = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return;
            if (received > maxBytes - read)
                return;
            received += read;
        }
    }

    private DateTimeOffset? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset receivedAt)
    {
        try
        {
            var value = response.Headers.RetryAfter;
            if (value?.Date is { } date)
                return date.ToUniversalTime();
            if (value?.Delta is { } delta && delta >= TimeSpan.Zero)
            {
                try { return receivedAt.Add(delta).ToUniversalTime(); }
                catch (ArgumentOutOfRangeException) { return DateTimeOffset.MaxValue; }
            }
        }
        catch (FormatException)
        {
            // Malformed Retry-After is equivalent to no server-directed delay.
        }
        return null;
    }

    private static string ReadSetting(IReadOnlyDictionary<string, string> settings, string name, string fallback) =>
        TryReadSetting(settings, name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool TryReadSetting(IReadOnlyDictionary<string, string>? settings, string name, out string value)
    {
        if (settings is not null && settings.TryGetValue(name, out value!))
            return true;
        if (settings is not null)
            foreach (var pair in settings)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
        value = string.Empty;
        return false;
    }

    private static bool ValidAttemptId(string? value) => value is { Length: 32 } &&
        Guid.TryParseExact(value, "N", out var parsed) && StringComparer.Ordinal.Equals(parsed.ToString("N"), value);

    private static bool IsLowerSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ValidUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static bool IsTransportUncertainty(Exception exception) => exception is HttpRequestException or IOException or SocketException;

    private static EtlHttpFetchResponse UncertainFetch() => new(null, null,
        new EtlHttpSourceFetchOutcome(null, null, InvalidPage: false));

    private static EtlHttpDeliveryResponse UncertainDelivery() => new(null, null);

    private static EtlBindingException InvalidRequest() =>
        new("etl.http.request-invalid", "The HTTP request is invalid or no longer within its allocated attempt.");

    private static EtlBindingException Unavailable() =>
        new("etl.binding-unavailable", "The scoped ETL HTTP binding or request is unavailable.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record BoundedBody(byte[]? Body, bool TooLarge);
    private sealed record QueryParameter(string Name, string Value);
}
