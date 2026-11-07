using System.Net;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Contracts;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Xunit;
using static Elsa.Activities.Http.Tests.SendHttpRequestTestSupport;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpActivityRequestExecutorTests
{
    [Fact]
    public void Request_captures_headers_and_connection_without_a_mutable_alias()
    {
        var headers = new Dictionary<string, string> { ["X-Input"] = "captured" };
        var connection = new ConnectionReference("original", "http");
        var request = new HttpActivityRequest(new Uri("https://example.test/resource"), "POST", "body", "text/plain", headers, connection);

        headers["X-Input"] = "changed";
        headers["X-Later"] = "added";

        Assert.Equal("captured", Assert.Single(request.RequestHeaders).Value);
        Assert.NotSame(connection, request.Connection);
        Assert.Equal(connection, request.Connection);
        var writableView = Assert.IsAssignableFrom<IDictionary<string, string>>(request.RequestHeaders);
        Assert.Throws<NotSupportedException>(() => writableView["X-Input"] = "changed");
    }

    [Fact]
    public async Task Ordinary_exchange_builds_then_applies_connection_and_captures_only_response()
    {
        var authorization = NewHeaderValue();
        var connection = new ConnectionReference("ordinary", "http");
        var steps = new List<string>();
        var applicator = new DelegateApplicator((request, reference, _, _) =>
        {
            steps.Add("credentials");
            Assert.Equal(connection, reference);
            Assert.Equal("authored", Assert.Single(request.Headers.GetValues("Authorization")));
            request.Headers.Remove("Authorization");
            request.Headers.Add("Authorization", authorization);
            return ValueTask.CompletedTask;
        });
        var responseBody = new TrackingContent("response");
        using var factory = new ClientFactory(new DelegateHandler(async (request, _) =>
        {
            steps.Add("transport");
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("payload", await request.Content!.ReadAsStringAsync());
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal(authorization, Assert.Single(request.Headers.GetValues("Authorization")));
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = responseBody };
            response.Headers.Add("X-Response", "safe");
            return response;
        }));
        var executor = new HttpActivityRequestExecutor(factory, applicator, new EffectGate());

        var result = await executor.ExecuteAsync(new HttpActivityRequest(new Uri("https://example.test/resource"), "POST", "payload",
            "application/json", new Dictionary<string, string> { ["Authorization"] = "authored" }, connection), Context());

        Assert.Equal(["credentials", "transport"], steps);
        Assert.Equal(HttpActivityConstants.HttpClientName, Assert.Single(factory.Names));
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("response", result.ResponseBody);
        Assert.Equal(["safe"], result.ResponseHeaders!["X-Response"]);
        Assert.DoesNotContain(result.ResponseHeaders.Keys, key => key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.True(responseBody.Disposed);
    }

    [Fact]
    public async Task Refreshable_unauthorized_exchange_rebuilds_body_and_credentials_once()
    {
        var refreshes = new List<bool>();
        var contents = new List<HttpContent>();
        var applicator = new DelegateApplicator((request, _, _, _) =>
        {
            refreshes.Add(request.Options.TryGetValue(HttpConnectionApplyOptions.RefreshCredentials, out var refresh) && refresh);
            request.Options.Set(HttpConnectionApplyOptions.HasRefreshableState, true);
            return ValueTask.CompletedTask;
        });
        var firstBody = new TrackingContent("unauthorized");
        var sends = 0;
        using var factory = new ClientFactory(new DelegateHandler(async (request, _) =>
        {
            contents.Add(request.Content!);
            Assert.Equal("repeatable", await request.Content!.ReadAsStringAsync());
            return ++sends == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = firstBody }
                : Respond(HttpStatusCode.Unauthorized, "still unauthorized");
        }));

        var result = await new HttpActivityRequestExecutor(factory, applicator, new EffectGate()).ExecuteAsync(
            new HttpActivityRequest(new Uri("https://example.test/resource"), "POST", "repeatable"), Context());

        Assert.Equal([false, true], refreshes);
        Assert.Equal(2, sends);
        Assert.NotSame(contents[0], contents[1]);
        Assert.True(firstBody.Disposed);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("still unauthorized", result.ResponseBody);
    }

    [Fact]
    public async Task Connection_refusal_disposes_built_request_and_never_sends()
    {
        var ownedBody = new TrackingContent("owned");
        var applicator = new DelegateApplicator((request, _, _, _) =>
        {
            request.Content?.Dispose();
            request.Content = ownedBody;
            throw new InvalidOperationException("connection refused");
        });
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Transport must not execute.")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new HttpActivityRequestExecutor(factory, applicator, new EffectGate())
            .ExecuteAsync(new HttpActivityRequest(new Uri("https://example.test/resource"), "GET"), Context()));

        Assert.True(ownedBody.Disposed);
    }

    [Fact]
    public async Task Replacement_executor_is_used_by_fresh_CLR_activation_before_connection_or_transport()
    {
        HttpActivityRequest? captured = null;
        ActivityExecutionContext? capturedContext = null;
        var replacement = new DelegateExecutor((request, context, cancellationToken) =>
        {
            Assert.True(cancellationToken.CanBeCanceled);
            captured = request;
            capturedContext = context;
            return ValueTask.FromResult(new SendHttpRequestResult(201, "replacement", new Dictionary<string, string[]>()));
        });
        await using var harness = NewBuilder()
            .WithFeature(services => services.Replace(ServiceDescriptor.Singleton<IHttpActivityRequestExecutor>(replacement)))
            .WithFeature(StubTransport(_ => throw new Xunit.Sdk.XunitException("Transport must not execute.")))
            .Build(ActivityExecutionId);

        await HttpActivityRequestExecutorCompositionTests.InitializeHttpAsync(harness.Services);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(expectedStatusCodes: [201],
            connection: new ConnectionReference("unavailable-original", "http"))));

        run.AssertOutcomes(NodeId, "201");
        run.AssertWorkflowCompleted();
        Assert.Equal("unavailable-original", captured!.Connection!.Name);
        Assert.Equal(NodeId, capturedContext!.ExecutableNodeId);
        Assert.False(string.IsNullOrWhiteSpace(capturedContext.AttemptId));
    }

    [Fact]
    public async Task Request_timeout_keeps_timeout_outcome()
    {
        var activity = new SendHttpRequest(OptionsFactory.Create(new HttpActivityOptions()),
            new DelegateExecutor((_, _, _) => throw new OperationCanceledException())) { Url = new Uri("https://example.test/resource") };

        var transition = await ((IActivity)activity).ExecuteAsync(Context());

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<SendHttpRequestResult>>(transition);
        Assert.Equal(HttpActivityOutcomes.Timeout, completion.Outcome);
        Assert.Equal(SendHttpRequestResult.NoResponse, completion.Result);
    }

    [Fact]
    public async Task Workflow_cancellation_propagates_instead_of_completing_as_timeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var activity = new SendHttpRequest(OptionsFactory.Create(new HttpActivityOptions()),
            new DelegateExecutor((_, _, _) => throw new OperationCanceledException(cancelled.Token))) { Url = new Uri("https://example.test/resource") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ((IActivity)activity).ExecuteAsync(Context(cancelled.Token)));
    }

    [Fact]
    public async Task Trusted_substitute_bypasses_original_connection_client_and_transport()
    {
        var gate = new EffectGate(RuntimeTestEffectMode.Substitute,
            JsonSerializer.SerializeToElement(new { statusCode = 202, body = "substituted", headers = new { ETag = new[] { "safe" } } }));
        var applicator = new DelegateApplicator((_, _, _, _) => throw new Xunit.Sdk.XunitException("Credentials must not resolve."));
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Transport must not execute.")));
        var request = new HttpActivityRequest(new Uri("https://original.example/resource"), "GET", connection: new("original", "http"));

        var result = await new HttpActivityRequestExecutor(factory, applicator, gate).ExecuteAsync(request, Context());

        Assert.Equal(202, result.StatusCode);
        Assert.Equal("substituted", result.ResponseBody);
        Assert.Empty(factory.Names);
        var admission = Assert.Single(gate.Requests);
        Assert.Equal(new RuntimeTestInvocation("workflow-http", "invocation-http", "attempt-http", NodeId), admission.Invocation);
        Assert.Equal("invocation-http:http", admission.OperationId);
        Assert.Matches("^[0-9a-f]{64}$", admission.RequestFingerprint);
        var completion = Assert.Single(gate.Completions);
        Assert.Equal(RuntimeTestEffectDisposition.Completed, completion.Disposition);
        Assert.Matches("^[0-9a-f]{64}$", completion.ResultFingerprint!);
        Assert.Equal(1, gate.Revalidations);
    }

    [Fact]
    public async Task Fresh_CLR_activity_uses_admitted_substitute_without_original_credentials()
    {
        // This ordinary one-node harness substitutes the trusted gate to exercise the HTTP adapter. It does not
        // admit a Testing manifest or qualify the separate closed-node policy for a real TestEntry.
        var gate = new EffectGate(RuntimeTestEffectMode.Substitute, JsonSerializer.SerializeToElement(new { statusCode = 201, body = "owned fixture" }));
        await using var harness = NewBuilder()
            .WithFeature(services => services.Replace(ServiceDescriptor.Singleton<IRuntimeTestEffectGate>(gate)))
            .WithFeature(StubTransport(_ => throw new Xunit.Sdk.XunitException("Transport must not execute.")))
            .Build(ActivityExecutionId);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(expectedStatusCodes: [201],
            connection: new ConnectionReference("unavailable-original", "http"))));

        run.AssertOutcomes(NodeId, "201");
        run.AssertWorkflowCompleted();
        Assert.Single(gate.Requests);
        Assert.Equal(1, gate.ActivationValidations);
        Assert.Equal(RuntimeTestEffectDisposition.Completed, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public async Task Explicit_test_system_uses_only_mapped_connection_and_real_destination()
    {
        await using var original = new LoopbackHttpServer(_ => (200, null));
        await using var testSystem = new LoopbackHttpServer(_ => (201, null));
        var mappedUrl = new Uri(testSystem.BaseAddress, "mapped");
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem,
            JsonSerializer.SerializeToElement(new { url = mappedUrl.AbsoluteUri, connection = new { name = "test-only", type = "http" },
                headers = new Dictionary<string, string> { ["Accept"] = "application/json" } }));
        var applied = new List<ConnectionReference>();
        var authorization = NewHeaderValue();
        var applicator = new DelegateApplicator((request, connection, _, _) =>
        {
            Assert.Equal(new ConnectionReference("test-only", "http"), connection);
            Assert.Equal(mappedUrl, request.RequestUri);
            applied.Add(connection!);
            Assert.False(request.Headers.Contains("Authorization"));
            request.Headers.Add("Authorization", authorization);
            return ValueTask.CompletedTask;
        });
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));
        var originals = new Dictionary<string, string>
        {
            ["Authorization"] = NewHeaderValue(), ["Cookie"] = "original=session", ["X-Api-Key"] = NewHeaderValue(),
            ["X-Private-Token"] = NewHeaderValue(), ["Accept"] = "original/accept"
        };

        var result = await new HttpActivityRequestExecutor(factory, applicator, gate).ExecuteAsync(
            new HttpActivityRequest(new Uri(original.BaseAddress, "original"), "GET", requestHeaders: originals,
                connection: new("production", "http")), Context());

        Assert.Equal(201, result.StatusCode);
        Assert.Single(applied);
        Assert.Empty(original.Requests);
        var delivered = Assert.Single(testSystem.Requests);
        Assert.Equal("/mapped", delivered.Path);
        Assert.Equal(authorization, delivered.Headers["Authorization"]);
        Assert.Equal("application/json", delivered.Headers["Accept"]);
        Assert.DoesNotContain("Cookie", delivered.Headers.Keys);
        Assert.DoesNotContain("X-Api-Key", delivered.Headers.Keys);
        Assert.DoesNotContain("X-Private-Token", delivered.Headers.Keys);
        Assert.DoesNotContain(delivered.Headers.Values, value => originals.Values.Contains(value, StringComparer.Ordinal));
        Assert.Empty(factory.Names);
        Assert.Equal(RuntimeTestEffectDisposition.Completed, Assert.Single(gate.Completions).Disposition);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task Test_system_redirect_never_reaches_original_target(int status)
    {
        await using var original = new LoopbackHttpServer(_ => (200, null));
        var originalUrl = new Uri(original.BaseAddress, "original-target");
        await using var testSystem = new LoopbackHttpServer(_ => (status, originalUrl));
        var mappedUrl = new Uri(testSystem.BaseAddress, "mapped");
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = mappedUrl.AbsoluteUri, connection = new { name = "test-only" }
        }));
        var credentials = NewHeaderValue();
        var applicator = new DelegateApplicator((request, connection, _, _) =>
        {
            Assert.Equal("test-only", connection!.Name);
            Assert.Equal(mappedUrl, request.RequestUri);
            request.Headers.Add("Authorization", credentials);
            return ValueTask.CompletedTask;
        });
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));

        var result = await new HttpActivityRequestExecutor(factory, applicator, gate).ExecuteAsync(
            new HttpActivityRequest(originalUrl, "GET", connection: new("production", "http")), Context());

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(credentials, Assert.Single(testSystem.Requests).Headers["Authorization"]);
        Assert.Empty(original.Requests);
        Assert.Empty(factory.Names);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Applicator_cannot_rewrite_mapped_URI_or_Host_before_transport(bool replaceHost)
    {
        await using var original = new LoopbackHttpServer(_ => (200, null));
        await using var testSystem = new LoopbackHttpServer(_ => (201, null));
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = new Uri(testSystem.BaseAddress, "mapped").AbsoluteUri, connection = new { name = "test-only" }
        }));
        var applicator = new DelegateApplicator((request, connection, _, _) =>
        {
            Assert.Equal("test-only", connection!.Name);
            if (replaceHost) request.Headers.Host = original.BaseAddress.Authority;
            else request.RequestUri = new Uri(original.BaseAddress, "rewritten");
            return ValueTask.CompletedTask;
        });
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await new HttpActivityRequestExecutor(factory, applicator, gate)
            .ExecuteAsync(new HttpActivityRequest(new Uri("https://original.example/resource"), "GET"), Context()));

        Assert.Empty(original.Requests);
        Assert.Empty(testSystem.Requests);
        Assert.Empty(factory.Names);
        Assert.Equal(RuntimeTestEffectDisposition.Unknown, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public async Task Test_system_client_excludes_ordinary_defaults_and_cookies_between_retries_and_operations()
    {
        var retryCount = 0;
        await using var server = new LoopbackHttpServer(request => new LoopbackHttpResponse(
            request.Path == "/test-retry" && Interlocked.Increment(ref retryCount) == 1 ? 401 : 201,
            Headers: new Dictionary<string, string> { ["Set-Cookie"] = request.Path == "/ordinary-seed" ? "ordinary=session; Path=/" : "test=attempt; Path=/" }));
        var services = new ServiceCollection();
        services.AddLogging();
        new ActivitiesHttpFeature().ConfigureServices(services);
        var ambientAuthorization = NewHeaderValue();
        services.AddHttpClient(HttpActivityConstants.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.Add("Authorization", ambientAuthorization);
            client.DefaultRequestHeaders.Add("X-Ambient-Secret", "ordinary-default");
        });
        await using var provider = services.BuildServiceProvider();
        var factory = new ObservingFactory(provider.GetRequiredService<IHttpClientFactory>());
        using (var ordinary = factory.CreateClient(HttpActivityConstants.HttpClientName))
        {
            using var seeded = await ordinary.GetAsync(new Uri(server.BaseAddress, "ordinary-seed"));
            using var checkedCookies = await ordinary.GetAsync(new Uri(server.BaseAddress, "ordinary-check"));
        }
        Assert.Contains("ordinary=session", server.Requests.Single(request => request.Path == "/ordinary-check").Headers["Cookie"], StringComparison.Ordinal);
        var firstCredential = NewHeaderValue();
        var refreshedCredential = NewHeaderValue();
        var refreshes = new List<bool>();
        var applicator = new DelegateApplicator((request, connection, _, _) =>
        {
            Assert.Equal("test-only", connection!.Name);
            Assert.Equal(server.BaseAddress.Authority, request.RequestUri!.Authority);
            var refresh = request.Options.TryGetValue(HttpConnectionApplyOptions.RefreshCredentials, out var flag) && flag;
            refreshes.Add(refresh);
            request.Options.Set(HttpConnectionApplyOptions.HasRefreshableState, true);
            request.Headers.Add("Authorization", refresh ? refreshedCredential : firstCredential);
            return ValueTask.CompletedTask;
        });
        foreach (var path in new[] { "test-retry", "test-next" })
        {
            var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
            {
                url = new Uri(server.BaseAddress, path).AbsoluteUri, connection = new { name = "test-only" }
            }));
            var context = new ActivityExecutionContext("workflow-http", path, "attempt-" + path, NodeId, CancellationToken.None);
            var result = await new HttpActivityRequestExecutor(factory, applicator, gate).ExecuteAsync(
                new HttpActivityRequest(new Uri("https://original.example/resource"), "GET"), context);
            Assert.Equal(201, result.StatusCode);
        }
        var testRequests = server.Requests.Where(request => request.Path.StartsWith("/test-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, testRequests.Length);
        Assert.All(testRequests, request =>
        {
            Assert.DoesNotContain("Cookie", request.Headers.Keys);
            Assert.DoesNotContain("X-Ambient-Secret", request.Headers.Keys);
            Assert.NotEqual(ambientAuthorization, request.Headers["Authorization"]);
        });
        Assert.Equal([false, true, false], refreshes);
        Assert.Equal(firstCredential, testRequests[0].Headers["Authorization"]);
        Assert.Equal(refreshedCredential, testRequests[1].Headers["Authorization"]);
        Assert.Single(factory.Names);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("X-Api-Key")]
    [InlineData("Host")]
    [InlineData("Connection")]
    [InlineData("Proxy-Authorization")]
    public async Task Unqualified_mapping_header_refuses_before_credentials(string name)
    {
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = "https://test.example/resource", connection = new { name = "test-only" }, headers = new Dictionary<string, string> { [name] = "not-allowed" }
        }));
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));
        var applicator = new DelegateApplicator((_, _, _, _) => throw new Xunit.Sdk.XunitException("Credentials must not resolve."));

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await new HttpActivityRequestExecutor(factory, applicator, gate)
            .ExecuteAsync(new HttpActivityRequest(new Uri("https://original.example/resource"), "GET"), Context()));

        Assert.Empty(factory.Names);
        Assert.Equal(RuntimeTestEffectDisposition.Refused, Assert.Single(gate.Completions).Disposition);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"connection\":{\"name\":\"test\"}}")]
    [InlineData("{\"url\":\"https://test.example/path\"}")]
    [InlineData("{\"url\":\"file:///private/resource\",\"connection\":{\"name\":\"test\"}}")]
    [InlineData("{\"url\":\"https://user:password@test.example/path\",\"connection\":{\"name\":\"test\"}}")]
    [InlineData("{\"url\":\"https://test.example/path\",\"connection\":{\"name\":\"test\",\"type\":\"other\"}}")]
    [InlineData("{\"url\":\"https://test.example/path\",\"url\":\"https://other.example/path\",\"connection\":{\"name\":\"test\"}}")]
    public async Task Missing_or_invalid_test_system_mapping_refuses_before_client_and_credentials(string configuration)
    {
        using var document = JsonDocument.Parse(configuration);
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, document.RootElement);
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Transport must not execute.")));
        var applicator = new DelegateApplicator((_, _, _, _) => throw new Xunit.Sdk.XunitException("Credentials must not resolve."));

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await new HttpActivityRequestExecutor(factory, applicator, gate)
            .ExecuteAsync(new HttpActivityRequest(new Uri("https://original.example/resource"), "GET", connection: new("original", "http")), Context()));

        Assert.Empty(factory.Names);
        Assert.Equal(RuntimeTestEffectDisposition.Refused, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public async Task Prior_unknown_operation_never_executes_or_rewrites_its_receipt()
    {
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new { })) { CanExecute = false };
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Transport must not execute.")));
        var applicator = new DelegateApplicator((_, _, _, _) => throw new Xunit.Sdk.XunitException("Credentials must not resolve."));

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await new HttpActivityRequestExecutor(factory, applicator, gate)
            .ExecuteAsync(new HttpActivityRequest(new Uri("https://original.example/resource"), "GET"), Context()));

        Assert.Empty(factory.Names);
        Assert.Empty(gate.Completions);
    }

    [Fact]
    public async Task Missing_trusted_policy_escapes_activity_business_outcomes_before_client_creation()
    {
        var gate = new EffectGate { RefuseBegin = true };
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Transport must not execute.")));
        var applicator = new DelegateApplicator((_, _, _, _) => throw new Xunit.Sdk.XunitException("Credentials must not resolve."));
        var activity = new SendHttpRequest(OptionsFactory.Create(new HttpActivityOptions()), new HttpActivityRequestExecutor(factory, applicator, gate))
        { Url = new Uri("https://original.example/resource") };

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await ((IActivity)activity).ExecuteAsync(Context()));

        Assert.Empty(factory.Names);
        Assert.Empty(gate.Completions);
    }

    [Fact]
    public async Task Scope_closure_before_credential_refresh_prevents_second_apply_and_send()
    {
        await using var testSystem = new LoopbackHttpServer(_ => (401, null));
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = new Uri(testSystem.BaseAddress, "mapped").AbsoluteUri, connection = new { name = "test-only" }
        })) { RefuseRevalidation = 2 };
        var credentialCalls = 0;
        var applicator = new DelegateApplicator((request, _, _, _) =>
        {
            credentialCalls++;
            request.Options.Set(HttpConnectionApplyOptions.HasRefreshableState, true);
            return ValueTask.CompletedTask;
        });
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await new HttpActivityRequestExecutor(factory, applicator, gate)
            .ExecuteAsync(new HttpActivityRequest(new Uri("https://original.example/resource"), "GET"), Context()));

        Assert.Equal(1, credentialCalls);
        Assert.Single(testSystem.Requests);
        Assert.Empty(factory.Names);
        Assert.Equal(RuntimeTestEffectDisposition.Unknown, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public async Task Transport_failure_remains_unknown_even_when_activity_returns_failed_outcome()
    {
        await using var testSystem = new LoopbackHttpServer(_ => new LoopbackHttpResponse(200, ContentLength: 16));
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = new Uri(testSystem.BaseAddress, "mapped").AbsoluteUri, connection = new { name = "test-only" }
        }));
        var applicator = new DelegateApplicator((_, _, _, _) => ValueTask.CompletedTask);
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));
        var activity = new SendHttpRequest(OptionsFactory.Create(new HttpActivityOptions()), new HttpActivityRequestExecutor(factory, applicator, gate))
        { Url = new Uri("https://original.example/resource") };

        var transition = await ((IActivity)activity).ExecuteAsync(Context());

        Assert.Equal(HttpActivityOutcomes.Failed, Assert.IsAssignableFrom<IActivityCompletionTransition>(transition).Outcome);
        Assert.Single(testSystem.Requests);
        Assert.Empty(factory.Names);
        Assert.Equal(RuntimeTestEffectDisposition.Unknown, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public async Task Cancellation_finalizes_unknown_with_an_independent_bounded_token()
    {
        using var cancellation = new CancellationTokenSource();
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = "https://test.example/resource", connection = new { name = "test-only" }
        }))
        {
            Completing = (_, token) => { Assert.True(token.CanBeCanceled); Assert.False(token.IsCancellationRequested); }
        };
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));
        var applicator = new DelegateApplicator((_, _, _, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var executor = new HttpActivityRequestExecutor(factory, applicator, gate);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await executor.ExecuteAsync(
            new HttpActivityRequest(new Uri("https://original.example/resource"), "GET"), Context(cancellation.Token), cancellation.Token));

        Assert.Equal(RuntimeTestEffectDisposition.Unknown, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public async Task Unavailable_disposition_write_escapes_activity_instead_of_reporting_a_business_failure()
    {
        var gate = new EffectGate(RuntimeTestEffectMode.TestSystem, JsonSerializer.SerializeToElement(new
        {
            url = "https://test.example/resource", connection = new { name = "test-only" }
        })) { Completing = (_, _) => throw new IOException("provider unavailable") };
        using var factory = new ClientFactory(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Ordinary transport must not execute.")));
        var executor = new HttpActivityRequestExecutor(factory,
            new DelegateApplicator((_, _, _, _) => throw new HttpRequestException("credential response lost")), gate);
        var activity = new SendHttpRequest(OptionsFactory.Create(new HttpActivityOptions()), executor) { Url = new Uri("https://original.example/resource") };

        await Assert.ThrowsAsync<RuntimeTestEffectUnavailableException>(async () => await ((IActivity)activity).ExecuteAsync(Context()));

        Assert.Equal(RuntimeTestEffectDisposition.Unknown, Assert.Single(gate.Completions).Disposition);
    }

    [Fact]
    public void Request_digest_orders_headers_and_covers_connection_body_url_and_timeout()
    {
        var url = new Uri("https://example.test/resource");
        HttpActivityRequest Request(string name = "original", string body = "body", TimeSpan? timeout = null) => new(url, "POST", body,
            requestHeaders: new Dictionary<string, string> { ["b"] = "two", ["a"] = "one" }, connection: new(name, "http"), timeout: timeout);
        var first = HttpActivityEffectData.Fingerprint(Request());
        var reordered = new HttpActivityRequest(url, "POST", "body", requestHeaders: new Dictionary<string, string> { ["a"] = "one", ["b"] = "two" },
            connection: new("original", "http"));

        Assert.Equal(first, HttpActivityEffectData.Fingerprint(reordered));
        Assert.NotEqual(first, HttpActivityEffectData.Fingerprint(Request(name: "other")));
        Assert.NotEqual(first, HttpActivityEffectData.Fingerprint(Request(body: "changed")));
        Assert.NotEqual(first, HttpActivityEffectData.Fingerprint(Request(timeout: TimeSpan.FromSeconds(1))));
        Assert.NotEqual(first, HttpActivityEffectData.Fingerprint(new HttpActivityRequest(new Uri("https://other.test/resource"), "POST", "body")));
    }

    private static ActivityExecutionContext Context(CancellationToken cancellationToken = default) =>
        new("workflow-http", "invocation-http", "attempt-http", NodeId, cancellationToken);

    private sealed class DelegateExecutor(Func<HttpActivityRequest, ActivityExecutionContext, CancellationToken, ValueTask<SendHttpRequestResult>> execute)
        : IHttpActivityRequestExecutor
    {
        public ValueTask<SendHttpRequestResult> ExecuteAsync(HttpActivityRequest request, ActivityExecutionContext context,
            CancellationToken cancellationToken = default) => execute(request, context, cancellationToken);
    }

    private sealed class DelegateApplicator(Func<HttpRequestMessage, ConnectionReference?, ActivityExecutionContext, CancellationToken, ValueTask> apply)
        : IHttpConnectionApplicator
    {
        public ValueTask ApplyAsync(HttpRequestMessage request, ConnectionReference? connection, ActivityExecutionContext context,
            CancellationToken cancellationToken = default) => apply(request, connection, context, cancellationToken);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client = new(handler);
        internal List<string> Names { get; } = [];
        public HttpClient CreateClient(string name) { Names.Add(name); return _client; }
        public void Dispose() => _client.Dispose();
    }

    private sealed class ObservingFactory(IHttpClientFactory inner) : IHttpClientFactory
    {
        internal List<string> Names { get; } = [];
        public HttpClient CreateClient(string name) { Names.Add(name); return inner.CreateClient(name); }
    }

    private sealed class TrackingContent(string body) : StringContent(body)
    {
        internal bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class EffectGate(RuntimeTestEffectMode mode = RuntimeTestEffectMode.Ordinary, JsonElement? configuration = null) : IRuntimeTestEffectGate
    {
        internal List<RuntimeTestEffectRequest> Requests { get; } = [];
        internal List<RuntimeTestEffectCompletion> Completions { get; } = [];
        internal int Revalidations { get; private set; }
        internal int ActivationValidations { get; private set; }
        internal int? RefuseRevalidation { get; init; }
        internal bool RefuseBegin { get; init; }
        internal bool CanExecute { get; init; } = true;
        internal Action<RuntimeTestEffectCompletion, CancellationToken>? Completing { get; init; }
        public ValueTask ValidateNodeAsync(RuntimeTestNodeRequest request, CancellationToken cancellationToken = default)
        {
            return request.Node.ExecutableNodeId == NodeId ? ValueTask.CompletedTask :
                ValueTask.FromException(new RuntimeTestEffectUnavailableException("fixture-node", "This fixture qualifies only its HTTP node."));
        }
        public ValueTask<RuntimeTestInvocation> ValidateActivityAsync(RuntimeTestActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivationValidations++;
            return ValueTask.FromResult(new RuntimeTestInvocation(request.WorkflowExecutionId, request.ActivityExecutionId, request.AttemptId, NodeId));
        }
        public ValueTask<RuntimeSecretReference> MapSecretAsync(RuntimeTestInvocation invocation, string inputKey,
            RuntimeSecretReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(reference);
        public ValueTask<RuntimeTestEffectAdmission> BeginAsync(RuntimeTestEffectRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (RefuseBegin) throw new RuntimeTestEffectUnavailableException("missing-policy", "The exact policy is unavailable.");
            return ValueTask.FromResult(new RuntimeTestEffectAdmission(request, mode,
                mode == RuntimeTestEffectMode.Substitute ? HttpActivityEffectData.SubstituteProfile : HttpActivityEffectData.TestSystemProfile,
                configuration ?? JsonSerializer.SerializeToElement(new { }), mode == RuntimeTestEffectMode.Ordinary ? null : "context",
                mode == RuntimeTestEffectMode.Ordinary ? null : "fingerprint", mode == RuntimeTestEffectMode.Ordinary ? null : "receipt", 1, CanExecute));
        }
        public ValueTask RevalidateAsync(RuntimeTestEffectAdmission admission, CancellationToken cancellationToken = default)
        {
            if (++Revalidations == RefuseRevalidation) throw new RuntimeTestEffectUnavailableException("closed", "The test scope closed.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CompleteAsync(RuntimeTestEffectCompletion completion, CancellationToken cancellationToken = default)
        {
            Completions.Add(completion);
            Completing?.Invoke(completion, cancellationToken);
            return ValueTask.CompletedTask;
        }
    }
}
