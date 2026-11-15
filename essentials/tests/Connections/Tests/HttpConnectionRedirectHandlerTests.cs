using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Elsa.Activities.Http.Constants;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class HttpConnectionRedirectHandlerTests
{
    private const string Origin = "https://origin.example";
    private const string Other = "https://other.example";
    private const string StartPath = "/start";
    private const string LandingPath = "/landing";

    [Theory]
    [InlineData("bearer")]
    [InlineData("basic")]
    [InlineData("api-key-header")]
    [InlineData("api-key-query")]
    public async Task SameOriginRedirect_KeepsConnectionCredentialsOnTheNextHop(string scheme)
    {
        var token = NewValue();
        var inner = new RecordingHandler(new Uri(Origin + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = await ApplyAsync(scheme, token, new Uri(Origin + StartPath), allowedHosts: "origin.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        AssertCredential(scheme, token, inner.Requests[0]);
        AssertCredential(scheme, token, inner.Requests[1]);
        Assert.Equal(LandingPath, inner.Requests[1].Path);
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("basic")]
    [InlineData("api-key-header")]
    [InlineData("api-key-query")]
    public async Task CrossOriginRedirect_StripsConnectionCredentialsOnTheNextHop(string scheme)
    {
        var token = NewValue();
        var inner = new RecordingHandler(new Uri(Other + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = await ApplyAsync(scheme, token, new Uri(Origin + StartPath), allowedHosts: "origin.example, other.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        AssertCredential(scheme, token, inner.Requests[0]);
        AssertNoCredential(scheme, inner.Requests[1]);
        Assert.Equal("other.example", inner.Requests[1].Host);
    }

    [Fact]
    public async Task ARedirectToAHostOutsideThePolicy_IsRefused()
    {
        var inner = new RecordingHandler(new Uri(Other + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = await ApplyAsync("bearer", NewValue(), new Uri(Origin + StartPath), allowedHosts: "origin.example");

        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => client.SendAsync(request));
        Assert.Equal(ConnectionFaultCodes.HostNotAllowed, refused.Code);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task HttpsToHttpRedirect_StripsConnectionCredentials()
    {
        var token = NewValue();
        var inner = new RecordingHandler(new Uri("http://origin.example" + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = await ApplyAsync("bearer", token, new Uri(Origin + StartPath), allowedHosts: "origin.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal($"Bearer {token}", inner.Requests[0].Authorization);
        Assert.Null(inner.Requests[1].Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Redirect307And308_KeepMethodAndBody(HttpStatusCode status)
    {
        var inner = new RecordingHandler(new Uri(Origin + LandingPath), status);
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + StartPath)
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal("POST", inner.Requests[1].Method);
        Assert.Equal("payload", inner.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    public async Task Redirect301302PostAnd303_RewriteToGetWithoutBody(HttpStatusCode status)
    {
        var inner = new RecordingHandler(new Uri(Origin + LandingPath), status);
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + StartPath)
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal("GET", inner.Requests[1].Method);
        Assert.True(string.IsNullOrEmpty(inner.Requests[1].Body));
    }

    [Fact]
    public async Task AllowAutoRedirectDisabled_ReturnsTheRedirectResponse()
    {
        var inner = new RecordingHandler(new Uri(Origin + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner, allowAutoRedirect: false);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Origin + StartPath);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task RedirectLoop_StopsAfterConfiguredHops()
    {
        var inner = new LoopingHandler();
        using var handler = new HttpConnectionRedirectHandler(inner, allowAutoRedirect: true, maxAutomaticRedirections: HttpConnectionRedirectHandler.DefaultMaxRedirects);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Origin + StartPath);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(HttpConnectionRedirectHandler.DefaultMaxRedirects + 1, inner.Sends);
    }

    [Fact]
    public async Task DifferentPortOnTheSameHost_StripsConnectionCredentials()
    {
        var token = NewValue();
        var inner = new RecordingHandler(new Uri("https://origin.example:8443" + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = await ApplyAsync("bearer", token, new Uri(Origin + StartPath), allowedHosts: "origin.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal($"Bearer {token}", inner.Requests[0].Authorization);
        Assert.Null(inner.Requests[1].Authorization);
        Assert.Equal(8443, inner.Requests[1].Port);
    }

    [Fact]
    public async Task NoConnection_StripsAuthorizationOnCrossOriginRedirect()
    {
        var inner = new RecordingHandler(new Uri(Other + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Origin + StartPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer supplied");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer supplied", inner.Requests[0].Authorization);
        Assert.Null(inner.Requests[1].Authorization);
    }

    [Fact]
    public async Task NoConnection_StripsAuthorizationOnSameOriginRedirect()
    {
        var inner = new RecordingHandler(new Uri(Origin + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Origin + StartPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer supplied");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer supplied", inner.Requests[0].Authorization);
        Assert.Null(inner.Requests[1].Authorization);
    }

    [Fact]
    public async Task NoConnection_StripsAuthorizationOnSameOriginRedirect_WhenConnectionsWorkflowsIsComposed()
    {
        await using var server = new SameOriginLoopback();
        await using var provider = ConnectionsTestComposition.CreateProvider(workflows: true);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpActivityConstants.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server.BaseAddress, StartPath.TrimStart('/')));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer supplied");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal("Bearer supplied", server.Requests[0].Authorization);
        Assert.Null(server.Requests[1].Authorization);
        Assert.Equal(LandingPath, server.Requests[1].Path);
    }

    [Fact]
    public async Task NoConnection_RefusesHttpsToHttpRedirect()
    {
        var inner = new RecordingHandler(new Uri("http://origin.example" + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, Origin + StartPath);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task QueryNameIsMatchedAsAParameterName()
    {
        var inner = new RecordingHandler(new Uri(Origin + LandingPath));
        using var handler = new HttpConnectionRedirectHandler(inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://origin.example" + StartPath + "?monkey=1");
        request.Options.Set(HttpConnectionRequestOptionKeys.Applied, new HttpConnectionApplication
        {
            ConnectionName = "acme",
            Hosts = AllowedHostsPolicy.Fixed("origin.example"),
            AllowInsecureHttp = false,
            QueryName = "key"
        });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    private static async Task<HttpRequestMessage> ApplyAsync(string scheme, string token, Uri url, string allowedHosts)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var connection = CreateConnection(scheme, token, allowedHosts);
        await Authenticator(scheme).ApplyAsync(request, connection);
        return request;
    }

    private static IHttpRequestAuthenticator Authenticator(string scheme) => scheme switch
    {
        "basic" => new BasicHttpRequestAuthenticator(),
        "api-key-header" or "api-key-query" => new ApiKeyHttpRequestAuthenticator(),
        _ => new BearerHttpRequestAuthenticator()
    };

    private static FakeResolvedConnection CreateConnection(string scheme, string token, string allowedHosts)
    {
        var hosts = AllowedHostsPolicy.Fixed(allowedHosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return scheme switch
        {
            "basic" => new FakeResolvedConnection(
                HttpAuthSchemes.Basic,
                hosts,
                ("password", token),
                settings: new Dictionary<string, string> { [HttpConnectionSettingKeys.Username] = "user" }),
            "api-key-header" => new FakeResolvedConnection(
                HttpAuthSchemes.ApiKey,
                hosts,
                ("apiKey", token),
                settings: new Dictionary<string, string>
                {
                    [HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Header,
                    [HttpConnectionSettingKeys.ApiKeyHeaderName] = "X-Api-Key"
                }),
            "api-key-query" => new FakeResolvedConnection(
                HttpAuthSchemes.ApiKey,
                hosts,
                ("apiKey", token),
                settings: new Dictionary<string, string>
                {
                    [HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Query,
                    [HttpConnectionSettingKeys.ApiKeyQueryName] = "api_key"
                }),
            _ => new FakeResolvedConnection(HttpAuthSchemes.Bearer, hosts, ("token", token))
        };
    }

    private static void AssertCredential(string scheme, string token, CapturedRequest request)
    {
        switch (scheme)
        {
            case "basic":
                var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"user:{token}"));
                Assert.Equal(basic, request.Authorization);
                break;
            case "api-key-header":
                Assert.Equal(token, request.ApiKeyHeader);
                break;
            case "api-key-query":
                Assert.Contains($"api_key={Uri.EscapeDataString(token)}", request.Query, StringComparison.Ordinal);
                break;
            default:
                Assert.Equal($"Bearer {token}", request.Authorization);
                break;
        }
    }

    private static void AssertNoCredential(string scheme, CapturedRequest request)
    {
        switch (scheme)
        {
            case "api-key-header":
                Assert.Null(request.ApiKeyHeader);
                break;
            case "api-key-query":
                Assert.DoesNotContain("api_key=", request.Query, StringComparison.OrdinalIgnoreCase);
                break;
            default:
                Assert.Null(request.Authorization);
                break;
        }
    }

    private static string NewValue() => $"tok{Guid.NewGuid():N}";

    private sealed class RecordingHandler(Uri landing, HttpStatusCode redirectStatus = HttpStatusCode.Found) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CapturedRequest.From(request));
            if (request.RequestUri?.AbsolutePath == StartPath)
            {
                var redirect = new HttpResponseMessage(redirectStatus);
                redirect.Headers.Location = landing;
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class LoopingHandler : HttpMessageHandler
    {
        public int Sends { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri(Origin + $"/hop-{Sends}");
            return Task.FromResult(redirect);
        }
    }

    private sealed record CapturedRequest(
        string Host,
        string Path,
        string Query,
        string? Authorization,
        string? ApiKeyHeader,
        string Method,
        string? Body,
        int Port)
    {
        public static CapturedRequest From(HttpRequestMessage request)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI.");
            request.Headers.TryGetValues("Authorization", out var authorization);
            request.Headers.TryGetValues("X-Api-Key", out var apiKey);
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new CapturedRequest(
                uri.IdnHost,
                uri.AbsolutePath,
                uri.Query,
                authorization?.FirstOrDefault(),
                apiKey?.FirstOrDefault(),
                request.Method.Method,
                body,
                uri.Port);
        }
    }

    private sealed class SameOriginLoopback : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<CapturedRequest> _requests = [];
        private readonly List<Task> _connections = [];
        private readonly Task _accepting;

        public SameOriginLoopback()
        {
            _listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _accepting = AcceptAsync();
        }

        public Uri BaseAddress { get; }
        public IReadOnlyList<CapturedRequest> Requests
        {
            get
            {
                lock (_requests)
                    return _requests.ToArray();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _accepting;
            Task[] connections;
            lock (_connections)
                connections = _connections.ToArray();
            await Task.WhenAll(connections);
            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }

                lock (_connections)
                    _connections.Add(ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_stop.Token);
                if (requestLine is null)
                    return;

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                {
                    var separator = line.IndexOf(':', StringComparison.Ordinal);
                    if (separator > 0)
                        headers[line[..separator]] = line[(separator + 1)..].Trim();
                }

                var path = requestLine.Split(' ')[1];
                headers.TryGetValue("Authorization", out var authorization);
                lock (_requests)
                    _requests.Add(new CapturedRequest("127.0.0.1", path, "", authorization, null, "GET", null, ((IPEndPoint)_listener.LocalEndpoint).Port));

                var status = path == StartPath ? 302 : 200;
                var response = new StringBuilder()
                    .Append(FormattableString.Invariant($"HTTP/1.1 {status} Stub\r\nContent-Length: 0\r\nConnection: close\r\n"));
                if (status == 302)
                    response.Append("Location: ").Append(new Uri(BaseAddress, LandingPath.TrimStart('/')).AbsoluteUri).Append("\r\n");
                response.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response.ToString()), _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }
    }

    private sealed class FakeResolvedConnection : IResolvedConnection
    {
        private readonly Dictionary<string, string> _secrets;

        public FakeResolvedConnection(
            string scheme,
            AllowedHostsPolicy hosts,
            (string Field, string Value) secret,
            IDictionary<string, string>? settings = null)
        {
            AuthScheme = scheme;
            Hosts = hosts;
            Settings = new Dictionary<string, string>(settings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            _secrets = new Dictionary<string, string>(StringComparer.Ordinal) { [secret.Field] = secret.Value };
        }

        public string TenantId => "tenant";
        public string Name => "acme";
        public string TypeName => ConnectionTypeNames.Http;
        public string AuthScheme { get; }
        public string Owner => ConnectionOwners.Tenant;
        public string Status => ConnectionStatuses.Ready;
        public IReadOnlyDictionary<string, string> Settings { get; }
        public AllowedHostsPolicy Hosts { get; }

        public ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default) =>
            new(new SensitiveValue(_secrets[fieldName]));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
