using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Source_authentication_is_resolved_again_for_a_durable_retry_and_rotation_reaches_the_wire()
    {
        const string page = "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":null}";
        var state = new AuthJourneyState("source-token-one");
        var getCount = 0;
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
        {
            if (request.Method != "GET")
                throw new InvalidOperationException("The source fixture accepts GET only.");
            if (Interlocked.Increment(ref getCount) == 1)
            {
                state.Rotate("source-token-two");
                return ValueTask.FromResult(new EtlHttpLoopbackResponse(503, ""));
            }
            return ValueTask.FromResult(new EtlHttpLoopbackResponse(200, page));
        });

        var tenant = new EtlScope("tenant-http-auth", "source-rotation");
        await using var files = new RuntimeFiles("id,name\n1,Ada\n", tenant);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors"))
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var run = await PrepareAuthenticatedConnectorRunAsync(files, endpoint, pipeline, state,
            authenticatedSource: true, authenticatedOutput: false, "http-source-auth-rotation", timeout.Token);

        var originalRun = await ReadConnectorRunAsync(run.Host.Services, tenant, run.Invocation.RunId, timeout.Token);
        var originalPin = originalRun.Bindings.Source.Revision;
        await ExecuteConnectorPartitionAttemptAsync(run.Host.Services, tenant, run.Invocation.RunId,
            "source-auth-first", timeout.Token);
        var firstFetch = await ReadSourceFetchAsync(run.Host.Services, timeout.Token);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, firstFetch.Phase);
        Assert.Single(firstFetch.Attempts);
        Assert.Equal(503, firstFetch.Attempts[0].Outcome!.StatusCode);
        Assert.Single(endpoint.Requests);
        Assert.Equal("Bearer source-token-one", endpoint.Requests[0].Headers["Authorization"]);

        run.Clock.Advance(TimeSpan.FromSeconds(2));
        await ExecuteConnectorPartitionAttemptAsync(run.Host.Services, tenant, run.Invocation.RunId,
            "source-auth-second", timeout.Token);

        var requests = endpoint.Requests;
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal("GET", request.Method));
        Assert.Equal("Bearer source-token-one", requests[0].Headers["Authorization"]);
        Assert.Equal("Bearer source-token-two", requests[1].Headers["Authorization"]);
        Assert.Equal(requests[0].RawTarget, requests[1].RawTarget);
        var completedFetch = await ReadSourceFetchAsync(run.Host.Services, timeout.Token);
        Assert.Equal(EtlConnectorHttpSourceFetchPhase.Captured, completedFetch.Phase);
        Assert.Equal(2, completedFetch.Attempts.Count);
        Assert.Equal(503, completedFetch.Attempts[0].Outcome!.StatusCode);
        Assert.Equal(completedFetch.Attempts[0].Attempt.RequestHash, completedFetch.Attempts[1].Attempt.RequestHash);
        Assert.Null(completedFetch.Attempts[1].Outcome);
        var capture = Assert.IsType<EtlHttpSourceFetchCapture>(completedFetch.Capture);
        Assert.Equal(completedFetch.Attempts[1].Attempt.RequestId, capture.RequestId);
        Assert.Equal(completedFetch.Attempts[1].Attempt.Fence, capture.Fence);
        Assert.Equal(completedFetch.Attempts[1].Attempt.RequestHash, capture.Page.RequestHash);
        Assert.Equal(completedFetch.PageSequence, capture.Page.PageSequence);
        Assert.Equal(1, capture.Page.RecordCount);
        Assert.Null(capture.Page.NextContinuation);
        var finalRun = await ReadConnectorRunAsync(run.Host.Services, tenant, run.Invocation.RunId, timeout.Token);
        Assert.Equal(originalPin, finalRun.Bindings.Source.Revision);
        AssertTenantScopedResolution(run.Resolver, tenant);
        AssertConnectionsDisposed(run.Resolver);
    }

    [Fact]
    public async Task Destination_authentication_is_resolved_again_for_a_durable_retry_with_stable_pin_effect_and_body()
    {
        var state = new AuthJourneyState("output-token-one");
        var postCount = 0;
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
        {
            if (request.Method != "POST" || request.RawTarget != "/output")
                throw new InvalidOperationException("The destination fixture accepts POST /output only.");
            if (Interlocked.Increment(ref postCount) == 1)
            {
                state.Rotate("output-token-two");
                return ValueTask.FromResult(new EtlHttpLoopbackResponse(503, ""));
            }
            return ValueTask.FromResult(new EtlHttpLoopbackResponse(204, ""));
        });

        var tenant = new EtlScope("tenant-http-auth", "output-rotation");
        await using var files = new RuntimeFiles("id,name\n1,Ada\n", tenant);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Source = new EtlCsvSource("source", EtlTestPipelines.Create().SourceSchema),
            Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors"))
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var run = await PrepareAuthenticatedConnectorRunAsync(files, endpoint, pipeline, state,
            authenticatedSource: false, authenticatedOutput: true, "http-output-auth-rotation", timeout.Token);

        var originalRun = await ReadConnectorRunAsync(run.Host.Services, tenant, run.Invocation.RunId, timeout.Token);
        var originalPin = originalRun.Bindings.Main.Revision;
        await ExecuteConnectorPartitionAttemptAsync(run.Host.Services, tenant, run.Invocation.RunId,
            "output-auth-first", timeout.Token);
        var pendingBatch = await ReadConnectorBatchAsync(run.Host.Services, timeout.Token);
        var retainedOutputJson = pendingBatch.OutputJson;
        var retainedOutputDigest = pendingBatch.OutputDigest;
        var retainedOutput = EtlConnectorRecordCodec.Read<EtlConnectorBatchOutput>(retainedOutputJson!);
        var retainedMain = Assert.IsType<EtlConnectorRouteOutput>(retainedOutput.Main);
        var firstDelivery = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(pendingBatch.HttpDeliveryJson!).Main!;
        Assert.Equal(EtlHttpDeliveryPhase.Waiting, firstDelivery.Phase);
        Assert.Single(firstDelivery.Attempts);
        Assert.Equal(503, firstDelivery.Attempts[0].Response!.StatusCode);
        Assert.Single(endpoint.Requests);
        Assert.Equal("Bearer output-token-one", endpoint.Requests[0].Headers["Authorization"]);

        run.Clock.Advance(TimeSpan.FromSeconds(2));
        await ExecuteConnectorPartitionAttemptAsync(run.Host.Services, tenant, run.Invocation.RunId,
            "output-auth-second", timeout.Token);

        var requests = endpoint.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal("Bearer output-token-one", requests[0].Headers["Authorization"]);
        Assert.Equal("Bearer output-token-two", requests[1].Headers["Authorization"]);
        Assert.Equal(requests[0].Body, requests[1].Body);
        Assert.Equal(retainedMain.EffectId, requests[0].Headers["Idempotency-Key"]);
        Assert.Equal(retainedMain.EffectId, requests[1].Headers["Idempotency-Key"]);

        var settledBatch = await ReadConnectorBatchAsync(run.Host.Services, timeout.Token);
        Assert.Equal(retainedOutputJson, settledBatch.OutputJson);
        Assert.Equal(retainedOutputDigest, settledBatch.OutputDigest);
        var completedDelivery = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(settledBatch.HttpDeliveryJson!).Main!;
        Assert.Equal(EtlHttpDeliveryPhase.Acknowledged, completedDelivery.Phase);
        Assert.Equal(2, completedDelivery.Attempts.Count);
        Assert.Equal(503, completedDelivery.Attempts[0].Response!.StatusCode);
        Assert.Equal(204, completedDelivery.Attempts[1].Response!.StatusCode);
        Assert.Equal(completedDelivery.Attempts[0].Attempt.EffectId, completedDelivery.Attempts[1].Attempt.EffectId);
        Assert.NotEqual(completedDelivery.Attempts[0].Attempt.RequestId, completedDelivery.Attempts[1].Attempt.RequestId);
        var finalRun = await ReadConnectorRunAsync(run.Host.Services, tenant, run.Invocation.RunId, timeout.Token);
        Assert.Equal(originalPin, finalRun.Bindings.Main.Revision);
        AssertTenantScopedResolution(run.Resolver, tenant);
        AssertConnectionsDisposed(run.Resolver);
    }

    [Theory]
    [InlineData("source", "revoked")]
    [InlineData("source", "settings-drift")]
    [InlineData("destination", "revoked")]
    [InlineData("destination", "settings-drift")]
    public async Task Revocation_or_connection_settings_drift_before_retry_refuses_send_without_fallback(
        string direction, string change)
    {
        var isSource = direction == "source";
        var state = new AuthJourneyState("token-before-change");
        var requestCount = 0;
        await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
        {
            if (isSource && (request.Method != "GET" || !request.RawTarget.StartsWith("/records?", StringComparison.Ordinal)))
                throw new InvalidOperationException("The source fixture accepts GET /records only.");
            if (!isSource && (request.Method != "POST" || request.RawTarget != "/output"))
                throw new InvalidOperationException("The destination fixture accepts POST /output only.");
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                state.ApplyConnectionChange(change);
                return ValueTask.FromResult(new EtlHttpLoopbackResponse(503, ""));
            }
            return ValueTask.FromResult(new EtlHttpLoopbackResponse(isSource ? 200 : 204,
                isSource ? "{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":null}" : ""));
        });

        var tenant = new EtlScope("tenant-http-auth", $"{direction}-{change}");
        await using var files = new RuntimeFiles("id,name\n1,Ada\n", tenant);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = isSource
            ? defaults with { Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")) }
            : defaults with
            {
                Source = new EtlCsvSource("source", EtlTestPipelines.Create().SourceSchema),
                Routes = new(new EtlHttpDestination("output"), new EtlJsonlDestination("errors"))
            };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var run = await PrepareAuthenticatedConnectorRunAsync(files, endpoint, pipeline, state,
            authenticatedSource: isSource, authenticatedOutput: !isSource, $"http-{direction}-auth-refusal-{change}", timeout.Token);

        var originalRun = await ReadConnectorRunAsync(run.Host.Services, tenant, run.Invocation.RunId, timeout.Token);
        var originalPin = isSource ? originalRun.Bindings.Source.Revision : originalRun.Bindings.Main.Revision;
        await ExecuteConnectorPartitionAttemptAsync(run.Host.Services, tenant, run.Invocation.RunId,
            $"{direction}-auth-refusal-first", timeout.Token);
        var retainedSourceFetchJson = isSource ? await ReadSourceFetchJsonAsync(run.Host.Services, timeout.Token) : null;
        var retainedBatch = isSource ? null : await ReadConnectorBatchAsync(run.Host.Services, timeout.Token);
        var retainedOutputJson = retainedBatch?.OutputJson;
        var retainedOutputDigest = retainedBatch?.OutputDigest;
        if (isSource)
        {
            var firstFetch = EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(retainedSourceFetchJson!);
            Assert.Equal(EtlConnectorHttpSourceFetchPhase.Waiting, firstFetch.Phase);
            Assert.Single(firstFetch.Attempts);
            Assert.Equal(503, firstFetch.Attempts[0].Outcome!.StatusCode);
            Assert.Equal("/records?cursor=seed", endpoint.Requests.Single().RawTarget);
        }
        else
        {
            var firstDelivery = EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(retainedBatch!.HttpDeliveryJson!).Main!;
            Assert.Equal(EtlHttpDeliveryPhase.Waiting, firstDelivery.Phase);
            Assert.Single(firstDelivery.Attempts);
            Assert.Equal(503, firstDelivery.Attempts[0].Response!.StatusCode);
        }
        Assert.Single(endpoint.Requests);
        Assert.Equal("Bearer token-before-change", endpoint.Requests[0].Headers["Authorization"]);

        run.Clock.Advance(TimeSpan.FromSeconds(2));
        await ExecuteConnectorPartitionAttemptAsync(run.Host.Services, tenant, run.Invocation.RunId,
            $"{direction}-auth-refusal-second", timeout.Token);

        Assert.Single(endpoint.Requests);
        var runView = await ReadConnectorRunAsync(run.Host.Services, tenant, run.Invocation.RunId, timeout.Token);
        Assert.Equal(originalPin, isSource ? runView.Bindings.Source.Revision : runView.Bindings.Main.Revision);
        Assert.Equal("etl.binding-unavailable", runView.Run.TerminalResult!.Diagnostic!.Code);
        AssertTenantScopedResolution(run.Resolver, tenant);
        AssertConnectionsDisposed(run.Resolver);
        if (isSource)
        {
            Assert.Equal(retainedSourceFetchJson, await ReadSourceFetchJsonAsync(run.Host.Services, timeout.Token));
            Assert.Empty(await ReadConnectorBatchesAsync(run.Host.Services, timeout.Token));
        }
        else
        {
            var afterRetry = await ReadConnectorBatchAsync(run.Host.Services, timeout.Token);
            Assert.Equal(retainedOutputJson, afterRetry.OutputJson);
            Assert.Equal(retainedOutputDigest, afterRetry.OutputDigest);
        }
        await using var afterTerminal = run.Host.Services.CreateAsyncScope();
        Assert.Null(await afterTerminal.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .ClaimConnectorPartitionAsync(tenant, run.Invocation.RunId, "post-terminal-no-fallback", timeout.Token));
    }

    private static async Task<AuthJourneyRun> PrepareAuthenticatedConnectorRunAsync(RuntimeFiles files,
        EtlHttpLoopbackServer endpoint, EtlPipelineV2 pipeline, AuthJourneyState state,
        bool authenticatedSource, bool authenticatedOutput, string runId, CancellationToken cancellationToken)
    {
        var resolver = new AuthJourneyResolver(files.Scope, endpoint.CreateUri("/").GetLeftPart(UriPartial.Authority), state);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureRuntime: runtime =>
            {
                if (authenticatedSource)
                {
                    runtime.FileBindings.RemoveAll(binding => binding.Role == "Source");
                    runtime.HttpBindings.Add(AuthenticationHttpBinding(files.Scope, endpoint, "source", "Source", "/records",
                        connectionName: "orders", sourceSeed: "/records?cursor=seed"));
                }
                if (authenticatedOutput)
                {
                    runtime.FileBindings.RemoveAll(binding => binding.Role == "MainOutput");
                    runtime.HttpBindings.Add(AuthenticationHttpBinding(files.Scope, endpoint, "output", "MainOutput", "/output",
                        connectionName: "orders"));
                }
            },
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(state.Clock));
                services.AddSingleton<IConnectionResolver>(resolver);
                services.AddSingleton<IConnectionTypeRegistry>(new AuthJourneyConnectionTypeRegistry());
                services.AddSingleton<IAuthenticationSchemeRegistry>(new AuthJourneySchemeRegistry());
                services.AddSingleton<IHttpRequestAuthenticator>(new AuthJourneyAuthenticator());
            });

        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(cancellationToken);
        var invocation = ConnectorInvocation(files.Scope, runId, pipeline);
        await files.AdmitAsync(host, invocation);
        await using (var operation = host.Services.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
            var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimConnectorPreparationAsync(
                files.Scope, "http-auth-preparation", cancellationToken));
            await operation.ServiceProvider.GetRequiredService<EtlConnectorExecution>()
                .PrepareAsync(preparation, cancellationToken);
        }
        return new(host, invocation, resolver, state.Clock);
    }

    private static EtlHostHttpBindingOptions AuthenticationHttpBinding(EtlScope scope, EtlHttpLoopbackServer endpoint,
        string alias, string role, string path, string? connectionName = null, string? sourceSeed = null) => new()
    {
        TenantId = scope.TenantId,
        PersistencePartition = scope.PersistencePartition,
        Alias = alias,
        Role = role,
        Revision = "http-auth-v1",
        Endpoint = endpoint.CreateUri(path).AbsoluteUri,
        AllowedPaths = [path],
        AllowHttp = true,
        ConnectionName = connectionName,
        Shards = sourceSeed is null ? [] : [new() { Id = "page", SeedUri = endpoint.CreateUri(sourceSeed).AbsoluteUri }]
    };

    private static async Task ExecuteConnectorPartitionAttemptAsync(IServiceProvider services, EtlScope scope,
        string runId, string owner, CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>();
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            scope, runId, owner, cancellationToken));
        await operation.ServiceProvider.GetRequiredService<EtlConnectorExecution>()
            .ExecutePartitionAsync(lease, cancellationToken);
    }

    private static async Task<EtlConnectorRunView> ReadConnectorRunAsync(IServiceProvider services, EtlScope scope,
        string runId, CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        return Assert.IsType<EtlConnectorRunView>(await operation.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
            .FindConnectorRunAsync(scope, runId, cancellationToken));
    }

    private static async Task<EtlConnectorBatchRecord> ReadConnectorBatchAsync(IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        return await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorBatches.AsNoTracking()
            .SingleAsync(cancellationToken);
    }

    private static async Task<List<EtlConnectorBatchRecord>> ReadConnectorBatchesAsync(IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        return await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorBatches.AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private static async Task<string> ReadSourceFetchJsonAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        return (await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorHttpRequests.AsNoTracking()
            .SingleAsync(cancellationToken)).FetchJson!;
    }

    private static async Task<EtlConnectorHttpSourceFetch> ReadSourceFetchAsync(IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var operation = services.CreateAsyncScope();
        var row = await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().ConnectorHttpRequests.AsNoTracking()
            .SingleAsync(cancellationToken);
        return EtlConnectorRecordCodec.Read<EtlConnectorHttpSourceFetch>(row.FetchJson!);
    }

    private static void AssertTenantScopedResolution(AuthJourneyResolver resolver, EtlScope expected)
    {
        Assert.NotEmpty(resolver.Requests);
        Assert.All(resolver.Requests, request =>
        {
            Assert.Equal(expected.TenantId, request.TenantId);
            Assert.Equal("orders", request.Name);
            Assert.Equal(ConnectionTypeNames.Http, request.RequiredType);
            Assert.Equal(ConnectionOwners.Tenant, request.Owner);
        });
    }

    private static void AssertConnectionsDisposed(AuthJourneyResolver resolver)
    {
        Assert.NotEmpty(resolver.Connections);
        Assert.All(resolver.Connections, connection => Assert.True(connection.Disposed));
    }

    private sealed record AuthJourneyRun(IHost Host, EtlInvocation Invocation, AuthJourneyResolver Resolver,
        AuthJourneyClock Clock);

    private sealed class AuthJourneyState(string token)
    {
        private readonly object _gate = new();
        private string _token = token;
        private string _connectionChange = "none";

        public AuthJourneyClock Clock { get; } = new();

        public AuthJourneySnapshot Snapshot()
        {
            lock (_gate)
                return new(_token, _connectionChange == "revoked", _connectionChange == "settings-drift");
        }

        public void Rotate(string nextToken)
        {
            lock (_gate)
                _token = nextToken;
        }

        public void ApplyConnectionChange(string change)
        {
            lock (_gate)
                _connectionChange = change;
        }
    }

    private sealed record AuthJourneySnapshot(string Token, bool Revoked, bool SettingsDrifted);

    private sealed class AuthJourneyClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    private sealed class AuthJourneyResolver(EtlScope scope, string baseUrl, AuthJourneyState state) : IConnectionResolver
    {
        private readonly ConcurrentQueue<ConnectionResolveRequest> _requests = new();
        private readonly ConcurrentQueue<AuthJourneyResolvedConnection> _connections = new();

        public IReadOnlyList<ConnectionResolveRequest> Requests => _requests.ToArray();
        public IReadOnlyList<AuthJourneyResolvedConnection> Connections => _connections.ToArray();

        public ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Enqueue(request);
            var snapshot = state.Snapshot();
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [HttpConnectionSettingKeys.BaseUrl] = baseUrl,
                [HttpConnectionSettingKeys.AllowedHosts] = "127.0.0.1",
                [HttpConnectionSettingKeys.AllowInsecureHttp] = "true"
            };
            if (snapshot.SettingsDrifted)
                settings["transportMode"] = "changed";
            var connection = new AuthJourneyResolvedConnection(scope.TenantId!, request.Name, snapshot.Token,
                snapshot.Revoked ? ConnectionStatuses.Revoked : ConnectionStatuses.Ready,
                settings, AllowedHostsPolicy.Fixed("127.0.0.1"));
            _connections.Enqueue(connection);
            return ValueTask.FromResult<IResolvedConnection>(connection);
        }
    }

    private sealed class AuthJourneyResolvedConnection(string tenantId, string name, string token,
        string status, IReadOnlyDictionary<string, string> settings, AllowedHostsPolicy hosts) : IResolvedConnection
    {
        private int _disposed;
        public string TenantId { get; } = tenantId;
        public string Name { get; } = name;
        public string TypeName => ConnectionTypeNames.Http;
        public string AuthScheme => HttpAuthSchemes.Bearer;
        public string Owner => ConnectionOwners.Tenant;
        public string Status { get; } = status;
        public IReadOnlyDictionary<string, string> Settings { get; } = settings;
        public AllowedHostsPolicy Hosts { get; } = hosts;
        public bool Disposed => Volatile.Read(ref _disposed) != 0;

        public ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(fieldName, HttpConnectionSecretFields.Token, StringComparison.Ordinal))
                throw new InvalidOperationException("The ETL authentication fixture requested an unexpected secret field.");
            return ValueTask.FromResult(new SensitiveValue(token));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            return ValueTask.CompletedTask;
        }

        public override string ToString() => nameof(AuthJourneyResolvedConnection);
    }

    private sealed class AuthJourneyAuthenticator : IHttpRequestAuthenticator
    {
        public string SchemeName => HttpAuthSchemes.Bearer;

        public async ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection,
            CancellationToken cancellationToken = default)
        {
            var token = await connection.GetSensitiveAsync(HttpConnectionSecretFields.Token, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Reveal());
        }
    }

    private sealed class AuthJourneyConnectionTypeRegistry : IConnectionTypeRegistry
    {
        private readonly IConnectionTypeProvider _provider = new AuthJourneyConnectionTypeProvider();
        public IConnectionTypeProvider? Find(string name) => string.Equals(name, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase)
            ? _provider : null;
        public IReadOnlyList<IConnectionTypeProvider> List() => [_provider];
    }

    private sealed class AuthJourneyConnectionTypeProvider : IConnectionTypeProvider
    {
        public ConnectionTypeDescriptor Descriptor { get; } = new(ConnectionTypeNames.Http, "HTTP", [HttpAuthSchemes.Bearer],
            [new(HttpConnectionSettingKeys.BaseUrl, "Base URL", "url", Required: true),
                new(HttpConnectionSettingKeys.AllowedHosts, "Allowed hosts", "text"),
                new(HttpConnectionSettingKeys.AllowInsecureHttp, "Allow insecure HTTP", "checkbox")],
            AllowedHostsPolicy.UserDeclared);

        public Task<ConnectionTestResult> TestAsync(IResolvedConnection connection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult { Succeeded = true });
    }

    private sealed class AuthJourneySchemeRegistry : IAuthenticationSchemeRegistry
    {
        private readonly IAuthenticationSchemeProvider _provider = new AuthJourneySchemeProvider();
        public IAuthenticationSchemeProvider? Find(string name) => string.Equals(name, HttpAuthSchemes.Bearer, StringComparison.OrdinalIgnoreCase)
            ? _provider : null;
        public IReadOnlyList<IAuthenticationSchemeProvider> List() => [_provider];
    }

    private sealed class AuthJourneySchemeProvider : IAuthenticationSchemeProvider
    {
        public AuthSchemeDescriptor Descriptor { get; } = new(HttpAuthSchemes.Bearer, "Bearer",
            [new(HttpConnectionSecretFields.Token, "Token", Sensitive: true, "password")],
            HasInteractiveAuthorization: false, HasRefreshableState: false, HasRevocation: false, RequiresWritableStore: false);
    }
}
