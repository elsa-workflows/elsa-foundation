using System.Globalization;
using System.Net;
using System.Text;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests;

public sealed partial class EtlHttpTransportTests
{
    [Fact]
    public async Task Fetch_retains_a_complete_200_body_and_uses_utc_receive_time()
    {
        const string json = "{\"records\":[{\"id\":1}]}";
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, json)));
        await using var binding = await ResolveAsync(server.CreateUri("/page"), "Source");
        using var transport = new EtlHttpTransport(TimeProvider.System);
        var attempt = NewFetchAttempt();

        var response = await transport.FetchAsync(binding, server.CreateUri("/page?cursor=a%2Bb"), attempt, 1024);

        Assert.Equal(json, Encoding.UTF8.GetString(response.Body!.Value.Span));
        Assert.Equal(TimeSpan.Zero, response.ResponseReceivedAtUtc!.Value.Offset);
        Assert.Equal(new EtlHttpSourceFetchOutcome(200, null, InvalidPage: false), response.Outcome);
        Assert.Equal("GET", Assert.Single(server.Requests).Method);
    }

    [Fact]
    public async Task Fetch_marks_oversized_200_as_invalid_and_keeps_other_statuses_as_metadata()
    {
        await using var tooLargeServer = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "123456")));
        await using var tooLargeBinding = await ResolveAsync(tooLargeServer.CreateUri("/page"), "Source");
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var tooLarge = await transport.FetchAsync(tooLargeBinding, tooLargeServer.CreateUri("/page"), NewFetchAttempt(), 4);

        Assert.Null(tooLarge.Body);
        Assert.Null(tooLarge.ResponseReceivedAtUtc);
        Assert.Equal(new EtlHttpSourceFetchOutcome(200, null, InvalidPage: true), tooLarge.Outcome);

        await using var rejectedServer = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(206, "partial")));
        await using var rejectedBinding = await ResolveAsync(rejectedServer.CreateUri("/page"), "Source");
        var rejected = await transport.FetchAsync(rejectedBinding, rejectedServer.CreateUri("/page"), NewFetchAttempt(), 1024);

        Assert.Null(rejected.Body);
        Assert.Null(rejected.ResponseReceivedAtUtc);
        Assert.Equal(206, rejected.Outcome.StatusCode);
        Assert.Null(rejected.Outcome.RetryAfter);
        Assert.False(rejected.Outcome.InvalidPage);

        await using var retryableServer = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(429, "",
                headers: new Dictionary<string, string> { ["Retry-After"] = "17" })));
        await using var retryableBinding = await ResolveAsync(retryableServer.CreateUri("/page"), "Source");
        var requestStarted = DateTimeOffset.UtcNow;
        var retryable = await transport.FetchAsync(retryableBinding, retryableServer.CreateUri("/page"), NewFetchAttempt(), 1024);
        var responseObserved = DateTimeOffset.UtcNow;

        Assert.Null(retryable.Body);
        Assert.Equal(429, retryable.Outcome.StatusCode);
        Assert.False(retryable.Outcome.InvalidPage);
        Assert.InRange(retryable.Outcome.RetryAfter!.Value, requestStarted.AddSeconds(17), responseObserved.AddSeconds(17));
    }

    [Fact]
    public async Task Fetch_parses_retry_after_http_date_as_exact_utc_time()
    {
        var expected = new DateTimeOffset(2099, 6, 5, 12, 0, 0, TimeSpan.Zero);
        var retryAfter = expected.ToString("r", CultureInfo.InvariantCulture);
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(429, "",
                headers: new Dictionary<string, string> { ["Retry-After"] = retryAfter })));
        await using var binding = await ResolveAsync(server.CreateUri("/page"), "Source");
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var response = await transport.FetchAsync(binding, server.CreateUri("/page"), NewFetchAttempt(), 1024);

        Assert.Null(response.Body);
        Assert.Equal(429, response.Outcome.StatusCode);
        Assert.Equal(expected, response.Outcome.RetryAfter);
        Assert.Equal(TimeSpan.Zero, response.Outcome.RetryAfter!.Value.Offset);
    }

    [Fact]
    public async Task Fetch_attempt_deadline_returns_uncertainty_without_a_response_receipt()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{}", delayBeforeBody: TimeSpan.FromSeconds(2))));
        await using var binding = await ResolveAsync(server.CreateUri("/page"), "Source");
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var timedOut = await transport.FetchAsync(binding, server.CreateUri("/page"), NewFetchAttempt(TimeSpan.FromMilliseconds(150)), 1024);

        Assert.Null(timedOut.Body);
        Assert.Null(timedOut.ResponseReceivedAtUtc);
        Assert.Null(timedOut.Outcome.StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_after_server_receives_request_is_propagated()
    {
        var requestArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await EtlHttpLoopbackServer.StartAsync(async (_, token) =>
        {
            requestArrived.TrySetResult();
            await releaseResponse.Task.WaitAsync(token);
            return new EtlHttpLoopbackResponse(200, "{}");
        });
        await using var binding = await ResolveAsync(server.CreateUri("/page"), "Source");
        using var transport = new EtlHttpTransport(TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        var operation = transport.FetchAsync(binding, server.CreateUri("/page"), NewFetchAttempt(TimeSpan.FromSeconds(5)), 1024,
            cancellation.Token).AsTask();

        try
        {
            await requestArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
        }
        finally
        {
            cancellation.Cancel();
            releaseResponse.TrySetResult();
            try { await operation; }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Requests_do_not_follow_redirects_or_reuse_cookies_between_attempts()
    {
        var calls = 0;
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
        {
            calls++;
            return ValueTask.FromResult(calls == 1
                ? new EtlHttpLoopbackResponse(302, "", new Dictionary<string, string>
                    { ["Location"] = "/redirect-target", ["Set-Cookie"] = "session=private" })
                : new EtlHttpLoopbackResponse(200, "{}"));
        });
        await using var binding = await ResolveAsync(server.CreateUri("/page"), "Source");
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var first = await transport.FetchAsync(binding, server.CreateUri("/page"), NewFetchAttempt(), 1024);
        var second = await transport.FetchAsync(binding, server.CreateUri("/page"), NewFetchAttempt(), 1024);

        Assert.Equal(302, first.Outcome.StatusCode);
        Assert.Equal(200, second.Outcome.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.DoesNotContain("Cookie", server.Requests[1].Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("/page", server.Requests[1].RawTarget);
    }

    [Fact]
    public async Task Delivery_posts_the_retained_bytes_with_effect_id_and_returns_only_response_metadata()
    {
        var body = Encoding.UTF8.GetBytes("[{\"effectId\":\"" + new string('b', 64) + "\"}]");
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(201, "response-secret")));
        await using var binding = await ResolveAsync(server.CreateUri("/deliver"), "MainOutput");
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var response = await transport.DeliverAsync(binding, NewDeliveryAttempt(), body, 8);

        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(body, request.Body);
        Assert.Equal(new string('b', 64), request.Headers[binding.IdempotencyHeader]);
        Assert.Equal("application/json", request.Headers["Content-Type"]);
        Assert.Equal(201, response.StatusCode);
        Assert.Null(response.RetryAfter);
        Assert.DoesNotContain("response-secret", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registered_custom_authenticator_can_add_bounded_header_and_query_fields()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{}")));
        var authenticator = new CustomAuthenticator((request, _) =>
        {
            request.Headers.TryAddWithoutValidation("X-Test-Signature", "signature-value");
            var builder = new UriBuilder(request.RequestUri!) { Query = "cursor=owned&signature=signature-value" };
            request.RequestUri = builder.Uri;
            return ValueTask.CompletedTask;
        });
        await using var binding = await ResolveAuthenticatedAsync(server.CreateUri("/page"), "Source", authenticator);
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var response = await transport.FetchAsync(binding, server.CreateUri("/page?cursor=owned"), NewFetchAttempt(), 1024);

        Assert.Equal(200, response.Outcome.StatusCode);
        var request = Assert.Single(server.Requests);
        Assert.Equal("signature-value", request.Headers["X-Test-Signature"]);
        Assert.Contains("cursor=owned&signature=signature-value", request.RawTarget, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticator_cannot_shadow_authored_query_or_replace_idempotency_header()
    {
        await using var sourceServer = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{}")));
        var sourceAuthenticator = new CustomAuthenticator((request, _) =>
        {
            request.RequestUri = new UriBuilder(request.RequestUri!) { Query = "cursor=attacker" }.Uri;
            return ValueTask.CompletedTask;
        });
        await using var sourceBinding = await ResolveAuthenticatedAsync(sourceServer.CreateUri("/page"), "Source", sourceAuthenticator);
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var queryError = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await transport.FetchAsync(sourceBinding, sourceServer.CreateUri("/page?cursor=owned"), NewFetchAttempt(), 1024));

        Assert.Equal("etl.binding-unavailable", queryError.Code);
        Assert.Empty(sourceServer.Requests);

        await using var outputServer = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "")));
        var outputAuthenticator = new CustomAuthenticator((request, _) =>
        {
            request.Headers.Remove("Idempotency-Key");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "replacement");
            return ValueTask.CompletedTask;
        });
        await using var outputBinding = await ResolveAuthenticatedAsync(outputServer.CreateUri("/deliver"), "MainOutput", outputAuthenticator);
        var idempotencyError = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await transport.DeliverAsync(outputBinding, NewDeliveryAttempt(), Encoding.UTF8.GetBytes("[]"), 0));

        Assert.Equal("etl.binding-unavailable", idempotencyError.Code);
        Assert.Empty(outputServer.Requests);
    }

    [Fact]
    public async Task Authenticator_cannot_change_authority_or_path_before_send()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "{}")));
        var authenticator = new CustomAuthenticator((request, _) =>
        {
            request.RequestUri = new Uri("http://127.0.0.1:1/other");
            return ValueTask.CompletedTask;
        });
        await using var binding = await ResolveAuthenticatedAsync(server.CreateUri("/page"), "Source", authenticator);
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await transport.FetchAsync(binding, server.CreateUri("/page"), NewFetchAttempt(), 1024));

        Assert.Equal("etl.http-endpoint-unavailable", error.Code);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Authenticator_cannot_replace_retained_request_body_before_send()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(201, "")));
        var authenticator = new CustomAuthenticator((request, _) =>
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("[{\"changed\":true}]"));
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return ValueTask.CompletedTask;
        });
        await using var binding = await ResolveAuthenticatedAsync(server.CreateUri("/deliver"), "MainOutput", authenticator);
        using var transport = new EtlHttpTransport(TimeProvider.System);

        var error = await Assert.ThrowsAsync<EtlBindingException>(async () =>
            await transport.DeliverAsync(binding, NewDeliveryAttempt(), Encoding.UTF8.GetBytes("[]"), 0));

        Assert.Equal("etl.binding-unavailable", error.Code);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_during_post_auth_body_verification_is_preserved()
    {
        await using var server = await EtlHttpLoopbackServer.StartAsync((_, _) =>
            ValueTask.FromResult(new EtlHttpLoopbackResponse(200, "")));
        using var cancellation = new CancellationTokenSource();
        var authenticator = new CustomAuthenticator((request, _) =>
        {
            request.Headers.TryAddWithoutValidation("X-Test-Signature", "signature-value");
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        });
        await using var binding = await ResolveAuthenticatedAsync(server.CreateUri("/deliver"), "MainOutput", authenticator);
        using var transport = new EtlHttpTransport(TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.DeliverAsync(binding, NewDeliveryAttempt(), Encoding.UTF8.GetBytes("[]"), 0,
                cancellation.Token));

        Assert.Empty(server.Requests);
    }

    private static async ValueTask<HostHttpResolvedBinding> ResolveAsync(Uri endpoint, string role)
    {
        var options = Options(endpoint, role);
        return await new HostHttpBindings([options]).ResolveCurrentAsync(new EtlScope("tenant", "partition"), options.Alias, role);
    }

    private static async ValueTask<HostHttpResolvedBinding> ResolveAuthenticatedAsync(Uri endpoint, string role,
        IHttpRequestAuthenticator authenticator)
    {
        var options = Options(endpoint, role);
        options.ConnectionName = "orders";
        var connection = new TestConnection(endpoint);
        var registry = new HostHttpBindings([options]).WithConnections(new TestResolver(connection), new TestConnectionTypes(),
            new TestSchemes(), [authenticator]);
        return await registry.ResolveCurrentAsync(new EtlScope("tenant", "partition"), options.Alias, role);
    }

    private static EtlHostHttpBindingOptions Options(Uri endpoint, string role)
    {
        var options = new EtlHostHttpBindingOptions
        {
            TenantId = "tenant", PersistencePartition = "partition", Alias = "binding", Role = role,
            Revision = "v1", Endpoint = endpoint.AbsoluteUri, AllowHttp = endpoint.Scheme == Uri.UriSchemeHttp
        };
        if (role == "Source")
            options.Shards.Add(new EtlHostHttpShardOptions { Id = "shard", SeedUri = endpoint.AbsoluteUri });
        return options;
    }

    private static EtlHttpSourceFetchAttempt NewFetchAttempt(TimeSpan? duration = null)
    {
        var started = DateTimeOffset.UtcNow;
        return new EtlHttpSourceFetchAttempt(Guid.NewGuid().ToString("N"), 1, 1, new string('a', 64), started,
            started.Add(duration ?? TimeSpan.FromSeconds(10)));
    }

    private static EtlHttpDeliveryAttempt NewDeliveryAttempt()
    {
        var started = DateTimeOffset.UtcNow;
        return new EtlHttpDeliveryAttempt(Guid.NewGuid().ToString("N"), 1, 1, new string('b', 64), started,
            started.AddSeconds(10));
    }

    private sealed class CustomAuthenticator(Func<HttpRequestMessage, CancellationToken, ValueTask> apply) : IHttpRequestAuthenticator
    {
        public string SchemeName => "test-signature";
        public ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return apply(request, cancellationToken);
        }
    }

    private sealed class TestConnection(Uri endpoint) : IResolvedConnection
    {
        public string TenantId => "tenant";
        public string Name => "orders";
        public string TypeName => ConnectionTypeNames.Http;
        public string AuthScheme => "test-signature";
        public string Owner => ConnectionOwners.Tenant;
        public string Status => ConnectionStatuses.Ready;
        public IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>
        {
            [HttpConnectionSettingKeys.BaseUrl] = endpoint.GetLeftPart(UriPartial.Authority),
            [HttpConnectionSettingKeys.AllowedHosts] = endpoint.IdnHost,
            [HttpConnectionSettingKeys.AllowInsecureHttp] = "true"
        };
        public AllowedHostsPolicy Hosts { get; } = AllowedHostsPolicy.Fixed(endpoint.IdnHost);
        public ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SensitiveValue("never-read"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override string ToString() => nameof(TestConnection);
    }

    private sealed class TestResolver(IResolvedConnection connection) : IConnectionResolver
    {
        public ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(connection);
    }

    private sealed class TestConnectionTypes : IConnectionTypeRegistry
    {
        private readonly IConnectionTypeProvider _provider = new TestConnectionType();
        public IConnectionTypeProvider? Find(string name) => name == ConnectionTypeNames.Http ? _provider : null;
        public IReadOnlyList<IConnectionTypeProvider> List() => [_provider];
    }

    private sealed class TestConnectionType : IConnectionTypeProvider
    {
        public ConnectionTypeDescriptor Descriptor { get; } = new(ConnectionTypeNames.Http, "HTTP", ["test-signature"], [],
            AllowedHostsPolicy.UserDeclared);
        public Task<ConnectionTestResult> TestAsync(IResolvedConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult { Succeeded = true });
    }

    private sealed class TestSchemes : IAuthenticationSchemeRegistry
    {
        private readonly IAuthenticationSchemeProvider _provider = new TestScheme();
        public IAuthenticationSchemeProvider? Find(string name) => name == "test-signature" ? _provider : null;
        public IReadOnlyList<IAuthenticationSchemeProvider> List() => [_provider];
    }

    private sealed class TestScheme : IAuthenticationSchemeProvider
    {
        public AuthSchemeDescriptor Descriptor { get; } = new("test-signature", "Test signature", [], false, false, false, false);
    }
}
