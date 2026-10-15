using System.Net.Http.Headers;
using System.Text;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class HttpAuthenticatorTests
{
    [Fact]
    public async Task Bearer_PrefixesScheme_AndRefusesDisallowedHost()
    {
        var authenticator = new BearerHttpRequestAuthenticator();
        var allowed = new FakeResolvedConnection(
            HttpAuthSchemes.Bearer,
            AllowedHostsPolicy.Fixed("api.example"),
            ("token", "abc123"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/v1");
        await authenticator.ApplyAsync(request, allowed);
        Assert.Equal("Bearer abc123", request.Headers.GetValues("Authorization").Single());

        using var refused = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/v1");
        var ex = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => authenticator.ApplyAsync(refused, allowed).AsTask());
        Assert.Equal(ConnectionFaultCodes.HostNotAllowed, ex.Code);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task Basic_SetsAuthorizationHeader()
    {
        var username = $"u{Guid.NewGuid():N}";
        var password = $"p{Guid.NewGuid():N}";
        var authenticator = new BasicHttpRequestAuthenticator();
        var connection = new FakeResolvedConnection(
            HttpAuthSchemes.Basic,
            AllowedHostsPolicy.Any,
            ("password", password),
            settings: new Dictionary<string, string> { [HttpConnectionSettingKeys.Username] = username });

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        await authenticator.ApplyAsync(request, connection);
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        Assert.Equal(expected, request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task Apply_RequiresHttpsUnlessAllowInsecureHttpIsSet()
    {
        var authenticator = new BearerHttpRequestAuthenticator();
        var connection = new FakeResolvedConnection(
            HttpAuthSchemes.Bearer,
            AllowedHostsPolicy.Any,
            ("token", "abc123"));

        using var insecure = new HttpRequestMessage(HttpMethod.Get, "http://api.example/v1");
        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => authenticator.ApplyAsync(insecure, connection).AsTask());
        Assert.Equal(ConnectionFaultCodes.InsecureHttp, refused.Code);

        var optedIn = new FakeResolvedConnection(
            HttpAuthSchemes.Bearer,
            AllowedHostsPolicy.Any,
            ("token", "abc123"),
            settings: new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowInsecureHttp] = "true" });
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "http://api.example/v1");
        await authenticator.ApplyAsync(allowed, optedIn);
        Assert.Equal("Bearer abc123", allowed.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task ApiKey_QueryPlacement_PutsKeyOnUri()
    {
        var authenticator = new ApiKeyHttpRequestAuthenticator();
        var connection = new FakeResolvedConnection(
            HttpAuthSchemes.ApiKey,
            AllowedHostsPolicy.Any,
            ("apiKey", "key-9"),
            settings: new Dictionary<string, string>
            {
                [HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Query,
                [HttpConnectionSettingKeys.ApiKeyQueryName] = "api_key"
            });

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/resource");
        await authenticator.ApplyAsync(request, connection);
        Assert.Contains("api_key=key-9", request.RequestUri!.Query, StringComparison.Ordinal);
        Assert.False(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task Bearer_RefusesCredentialWithLineBreak()
    {
        var authenticator = new BearerHttpRequestAuthenticator();
        var connection = new FakeResolvedConnection(
            HttpAuthSchemes.Bearer,
            AllowedHostsPolicy.Any,
            ("token", "abc\r\nX-Injected: 1"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        await Assert.ThrowsAsync<InvalidOperationException>(() => authenticator.ApplyAsync(request, connection).AsTask());
        Assert.False(request.Headers.Contains("Authorization"));
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
