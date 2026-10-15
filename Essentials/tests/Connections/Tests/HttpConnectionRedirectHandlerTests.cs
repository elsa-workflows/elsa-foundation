using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
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
