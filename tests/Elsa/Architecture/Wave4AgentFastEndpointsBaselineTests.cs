using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Captures the immutable HTTP/OpenAPI contract for Agent while its registrations are still
/// implemented by FastEndpoints. The capture host intentionally uses fixed domain services so the
/// wire observations are stable enough to compare with the migrated Minimal API host.
/// </summary>
[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentFastEndpointsBaselineTests
{
    private static readonly string BaselineDirectory = Path.Join(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task FastEndpoints_baseline_contains_exactly_eleven_agent_registrations()
    {
        var output = Environment.GetEnvironmentVariable("WAVE4_BASELINE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var captured = await CaptureAsync();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Join(output, "wave4-agent-http-fastendpoints.json"), CompatibilityJson.Serialize(captured.Http));
            File.WriteAllText(Path.Join(output, "wave4-agent-openapi-fastendpoints.json"), CompatibilityJson.Serialize(captured.OpenApi));
            return;
        }

        var baseline = BaselineFile.Load<HttpCompatibilityObservation[]>(
            Path.Join(BaselineDirectory, "wave4-agent-http-fastendpoints.json"));
        var openApi = BaselineFile.Load<OpenApiEvidenceDocument>(
            Path.Join(BaselineDirectory, "wave4-agent-openapi-fastendpoints.json"));

        Assert.Equal(11, baseline.Select(item => item.Endpoint).Distinct().Count());
        Assert.Equal(11, openApi.Operations.Count);
        Assert.Contains(baseline, item => item.Endpoint.Route.Value.EndsWith("/stream", StringComparison.Ordinal)
            && item.Streaming.Contains("data:", StringComparison.Ordinal)
            && item.Streaming.Contains("\n", StringComparison.Ordinal));
    }

    public static IReadOnlyList<HttpCompatibilityCase> Cases { get; } =
    [
        Case(HttpMethod.Get, "bootstrap", "bootstrap", null),
        Case(HttpMethod.Post, "sessions", "sessions", "{}"),
        Case(HttpMethod.Get, "sessions/session-1", "session", null),
        Case(HttpMethod.Post, "sessions/session-1/messages", "message", "{\"message\":\"hello\"}"),
        Case(HttpMethod.Post, "sessions/session-1/turns/turn-1/cancel", "cancel", "{\"sessionId\":\"session-1\",\"turnId\":\"turn-1\"}"),
        new(new("/_elsa/agent/sessions/{sessionId}/stream", "GET"), "stream", () => Request(HttpMethod.Get, "/_elsa/agent/sessions/session-1/stream"))
        {
            Binding = "route=sessionId",
            BoundedStreaming = true,
            MaxStreamBytes = 4096,
            MaxStreamFrames = 32
        },
        Case(HttpMethod.Post, "feedback", "feedback", "{\"sessionId\":\"session-1\",\"rating\":5}"),
        Case(HttpMethod.Post, "proposals/proposal-1/approve", "approve", "{\"proposalId\":\"proposal-1\"}"),
        Case(HttpMethod.Post, "proposals/proposal-1/deny", "deny", "{\"proposalId\":\"proposal-1\",\"reason\":\"no\"}"),
        Case(HttpMethod.Post, "proposals/proposal-1/execute", "execute", "{\"proposalId\":\"proposal-1\"}"),
        Case(HttpMethod.Get, "audit", "audit", null)
    ];

    private static HttpCompatibilityCase Case(HttpMethod method, string path, string name, string? body)
    {
        var route = $"/_elsa/agent/{path}";
        return new(new(route, method.Method), name, () => Request(method, route, body))
        {
            Binding = path.Contains("{", StringComparison.Ordinal) ? "route=sessionId" : ""
        };
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(Wave4AgentHost.IdentityHeader, "wildcard");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>Creates the old FE host and captures its consumed HTTP/OpenAPI evidence.</summary>
    public static async Task<(IReadOnlyList<HttpCompatibilityObservation> Http, OpenApiEvidenceDocument OpenApi)> CaptureAsync()
    {
        await using var host = await Wave4AgentHost.StartAsync();
        var observations = new List<HttpCompatibilityObservation>(Cases.Count);
        foreach (var testCase in Cases)
            observations.Add(await HttpEvidenceCapture.CaptureAsync(host.Client, testCase));

        var rawOpenApi = await host.GetOpenApiAsync();
        return (observations, OpenApiEvidenceCapture.Capture(rawOpenApi));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave4AgentFastEndpointsCollection
{
    public const string Name = "wave4-agent-fastendpoints";
}

internal sealed class Wave4AgentHost : IAsyncDisposable
{
    public const string IdentityHeader = "X-Wave4-Agent-Identity";
    private readonly IHost host;

    private Wave4AgentHost(IHost host) => this.host = host;

    public HttpClient Client => host.GetTestClient();

    public static async Task<Wave4AgentHost> StartAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "testhost");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication(Wave4AgentAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, Wave4AgentAuthenticationHandler>(
                            Wave4AgentAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            Wave4AgentAuthenticationHandler.SchemeName
                        });
                    services.AddOpenApi();
                    new FoundationAgentApiFeature().ConfigureServices(services);
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(FoundationAgentApiFeature).Assembly];
                        options.Filter = type => type.Namespace?.StartsWith("Elsa.Agent.Api.Endpoints", StringComparison.Ordinal) == true;
                    });

                    services.Replace(ServiceDescriptor.Singleton<IAgentSessionService, Wave4FixedSessionService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentPolicyEvaluator, Wave4FixedPolicyEvaluator>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentContextCollector, Wave4FixedContextCollector>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentContextSanitizer, Wave4FixedContextSanitizer>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentCapabilityCatalog, Wave4FixedCapabilityCatalog>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentProviderRegistry, Wave4FixedProviderRegistry>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentStreamingService, Wave4FixedStreamingService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentFeedbackService, Wave4FixedFeedbackService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentProposalService, Wave4FixedProposalService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentAuditReader, Wave4FixedAuditReader>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink, Wave4FixedAuditSink>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry, Wave4FixedTurnRegistry>());
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                        {
                            using var scope = endpoints.ServiceProvider.CreateScope();
                            foreach (var configurator in scope.ServiceProvider.GetServices<CShells.FastEndpoints.Contracts.IFastEndpointsConfigurator>())
                                configurator.Configure(config);
                        });
                        endpoints.MapOpenApi();
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return new(host);
    }

    public async Task<string> GetOpenApiAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }
}

internal sealed class Wave4AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Wave4AgentBaseline";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers[Wave4AgentHost.IdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(identity))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = identity.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var grant = parts[0];
        var actorId = parts.Length > 1 ? parts[1] : "actor-1";
        var tenantId = parts.Length > 2 ? parts[2] : "tenant-1";
        var permissions = grant switch
        {
            "wildcard" => new[] { PermissionKey.Wildcard },
            "use" => new[] { "agent.use" },
            "proposals" => new[] { "agent.proposals" },
            "audit" => new[] { "agent.audit" },
            _ => Array.Empty<string>()
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actorId),
            new Claim(IdentityClaimTypes.TenantId, tenantId),
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            ..permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
        ], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

/// <summary>Runs the identical fixed-service fixture with the Agent Minimal API mapper.</summary>
internal sealed class Wave4AgentMinimalApiHost : IAsyncDisposable
{
    private readonly IHost host;

    private Wave4AgentMinimalApiHost(IHost host) => this.host = host;

    public HttpClient Client => host.GetTestClient();

    public static async Task<Wave4AgentMinimalApiHost> StartAsync(IAgentStreamingService? streaming = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "testhost");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication(Wave4AgentAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, Wave4AgentAuthenticationHandler>(
                            Wave4AgentAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            Wave4AgentAuthenticationHandler.SchemeName
                        });
                    services.AddOpenApi();
                    new FoundationAgentApiFeature().ConfigureServices(services);
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(Wave4FastEndpointsCanary).Assembly];
                        options.Filter = type => type == typeof(Wave4FastEndpointsCanary);
                    });
                    services.Replace(ServiceDescriptor.Singleton<IAgentSessionService, Wave4FixedSessionService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentPolicyEvaluator, Wave4FixedPolicyEvaluator>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentContextCollector, Wave4FixedContextCollector>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentContextSanitizer, Wave4FixedContextSanitizer>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentCapabilityCatalog, Wave4FixedCapabilityCatalog>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentProviderRegistry, Wave4FixedProviderRegistry>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentStreamingService>(streaming ?? new Wave4FixedStreamingService()));
                    services.Replace(ServiceDescriptor.Singleton<IAgentFeedbackService, Wave4FixedFeedbackService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentProposalService, Wave4FixedProposalService>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentAuditReader, Wave4FixedAuditReader>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink, Wave4FixedAuditSink>());
                    services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry, Wave4FixedTurnRegistry>());
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints();
                        AgentApi.MapAgentApi(endpoints);
                        endpoints.MapOpenApi();
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return new(host);
    }

    public async Task<string> GetOpenApiAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }
}

internal sealed class Wave4FastEndpointsCanary : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "/_wave4/fast-endpoints-canary";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions("agent.use");
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("fast-endpoints-canary", cancellationToken);
}

internal static class Wave4AgentFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    public static readonly AgentPolicy Policy = AgentPolicy.Default;
    public static readonly AgentSession Session = new(
        "session-1", "Agent session", "tenant-1", "actor-1", "conversation-1", "provider-1", "explain",
        Policy, AgentSessionStatus.Active, Now, Now, null, new Dictionary<string, string>());
    public static readonly AgentMessage Message = new(
        "message-1", "session-1", AgentRole.User, "hello", AgentMessageStatus.Pending, null, Now, null, null, [], [], []);
    public static readonly AgentActionProposal Proposal = new(
        "proposal-1", "session-1", null, "workflow.explain", "answer", "Proposal", "Summary", AgentRisk.ReadOnly,
        null, [], [], [], null, [], null, null, false, AgentActionProposalStatus.AwaitingApproval, null, null, Now, Now);
}

internal sealed class Wave4FixedSessionService : IAgentSessionService
{
    public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Wave4AgentFixtures.Session);
    public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<AgentSession?>(sessionId == "session-1" ? Wave4AgentFixtures.Session : null);
    public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Wave4AgentFixtures.Message);
    public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AgentMessage>>([Wave4AgentFixtures.Message]);
    public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => Task.FromResult<AgentMessage?>(messageId == "message-1" ? Wave4AgentFixtures.Message : null);
    public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default) => Task.FromResult<AgentMessage?>(Wave4AgentFixtures.Message);
    public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AgentContextAttachment>>([]);
}

internal sealed class Wave4FixedPolicyEvaluator : IAgentPolicyEvaluator
{
    public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AgentPolicyDecision(true, []));
    public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AgentPolicyDecision(true, []));
    public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AgentPolicyDecision(true, []));
}

internal sealed class Wave4FixedContextCollector : IAgentContextCollector
{
    public Task<AgentResult<IReadOnlyCollection<AgentContextAttachment>>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default) => Task.FromResult(AgentResult<IReadOnlyCollection<AgentContextAttachment>>.Success([]));
}

internal sealed class Wave4FixedContextSanitizer : IAgentContextSanitizer
{
    public ValueTask<IReadOnlyCollection<AgentContextAttachment>> SanitizeAsync(IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) => ValueTask.FromResult(attachments);
}

internal sealed class Wave4FixedCapabilityCatalog : IAgentCapabilityCatalog
{
    public Task<IReadOnlyCollection<AgentCapability>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AgentCapability>>(
            [new("workflow.explain", "agent", "Explain", "Explain a workflow.", AgentCapabilityKind.Answer, AgentRisk.ReadOnly, ["studio"], ["agent.use"], ["workflow.definition"])]);
}

internal sealed class Wave4FixedProviderRegistry : IAgentProviderRegistry
{
    public IAgentProvider? Active { get; } = new Wave4FixedProvider();
}

internal sealed class Wave4FixedProvider : IAgentProvider
{
    public string ProviderId => "provider-1";
    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => Task.FromResult(new AgentProviderSession("provider-session-1", ProviderId, new Dictionary<string, string>()));
    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new AgentStreamEvent("event-1", AgentStreamEventKind.Completed, "done", null, null, Wave4AgentFixtures.Now);
    }
    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new AgentToolApprovalResult(true, "approved"));
    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding, [AgentProviderOperation.Chat, AgentProviderOperation.Streaming], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
}

internal sealed class Wave4FixedStreamingService : IAgentStreamingService
{
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new AgentStreamEvent("event-1", AgentStreamEventKind.MessageDelta, "hello", null, null, Wave4AgentFixtures.Now);
        yield return new AgentStreamEvent("event-2", AgentStreamEventKind.Completed, null, null, null, Wave4AgentFixtures.Now);
    }
}

internal sealed class Wave4FixedFeedbackService : IAgentFeedbackService
{
    public Task<AgentFeedback> AddAsync(AgentFeedback feedback, CancellationToken cancellationToken = default) => Task.FromResult(new AgentFeedback("feedback-1", "session-1", null, "positive", null, "actor-1", Wave4AgentFixtures.Now));
}

internal sealed class Wave4FixedProposalService : IAgentProposalService
{
    public Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default) => Task.FromResult(Wave4AgentFixtures.Proposal);
    public Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default) => Task.FromResult<AgentActionProposal?>(proposalId == "proposal-1" ? Wave4AgentFixtures.Proposal : null);
    public Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default) => Task.FromResult(AgentResult<AgentActionProposal>.Success(Wave4AgentFixtures.Proposal with { Status = AgentActionProposalStatus.Approved, ApprovedBy = actorId, ApprovedAt = Wave4AgentFixtures.Now }));
    public Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default) => Task.FromResult(AgentResult<AgentActionProposal>.Success(Wave4AgentFixtures.Proposal with { Status = AgentActionProposalStatus.Denied }));
    public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default) => Task.FromResult(AgentResult<AgentProposalExecutionResult>.Success(new AgentProposalExecutionResult("proposal-1", true, "workflow", "workflow-1", "executed")));
}

internal sealed class Wave4FixedAuditReader : IAgentAuditReader
{
    public Task<IReadOnlyCollection<AgentAuditEvent>> ListAsync(string? sessionId = null, int? take = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AgentAuditEvent>>([]);
}

internal sealed class Wave4FixedAuditSink : IAgentAuditSink
{
    public Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class Wave4FixedTurnRegistry : IAgentTurnRegistry
{
    public CancellationToken Register(string turnId) => CancellationToken.None;
    public bool Cancel(string turnId) => false;
    public void Unregister(string turnId) { }
}
