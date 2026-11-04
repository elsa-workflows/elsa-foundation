using System.Security.Claims;
using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent.Api;
using Elsa.Agent.Api.Extensions;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Agent.Workflows;
using Elsa.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

public sealed class AgentFeatureRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_principal_sources_abort_real_shell_activation_including_copied_root_defaults(bool inheritedDefaults)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        if (inheritedDefaults) services.AddFoundationAgentAbstractions();
        services.AddCShells(builder => builder
            .WithAssemblies(typeof(AmbiguousPrincipalFeature).Assembly, typeof(EventsFeature).Assembly)
            .AddShell("agent-principal-conflict", shell => shell.WithFeature<AmbiguousPrincipalFeature>()));
        await using var root = services.BuildServiceProvider();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync("agent-principal-conflict"));
        var causes = new List<string>();
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException) causes.Add(cause.Message);
        var diagnostic = string.Join(" ", causes);
        Assert.Contains(nameof(IAgentPrincipalSource), diagnostic);
        Assert.Contains("at most one", diagnostic);
        Assert.Contains("Replace", diagnostic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Principal_api_registration_is_scoped_idempotent_and_independent_of_base_order(bool apiFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        if (apiFirst) services.AddFoundationAgentApi();
        services.AddFoundationAgentAbstractions();
        services.AddFoundationAgentApi();
        services.AddFoundationAgentAbstractions();
        services.AddFoundationAgentApi();
        var registration = Assert.Single(services, item => item.ServiceType == typeof(IAgentPrincipalSource));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();
        foreach (var initializer in scope.ServiceProvider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(CancellationToken.None);
        Assert.Single(scope.ServiceProvider.GetServices<IAgentPrincipalSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_principal_integration_survives_api_configuration_and_explicit_replacement(bool replaceAfter)
    {
        using var root = AgentTestServices.Create(services =>
        {
            if (replaceAfter)
            {
                services.AddFoundationAgentApi();
                services.Replace(ServiceDescriptor.Scoped<IAgentPrincipalSource, CustomPrincipalSource>());
            }
            else services.AddScoped<IAgentPrincipalSource, CustomPrincipalSource>();
            services.AddFoundationAgentApi();
            services.AddFoundationAgentAbstractions();
            services.AddFoundationAgentApi();
        });
        using var scope = root.CreateScope();
        Assert.IsType<CustomPrincipalSource>(Assert.Single(scope.ServiceProvider.GetServices<IAgentPrincipalSource>()));
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
    }

    [Fact]
    public void Legacy_base_has_no_principal_source_and_keyed_sources_do_not_supply_the_replacement()
    {
        using var root = AgentTestServices.Create(services =>
            services.AddKeyedSingleton<IAgentPrincipalSource, CustomPrincipalSource>("unrelated"));
        using var scope = root.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetServices<IAgentPrincipalSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("singleton")]
    [InlineData("transient")]
    public void Invalid_principal_replacement_metadata_refuses_default_paths_before_module_use(string invalid)
    {
        using var root = AgentTestServices.Create(services =>
        {
            services.AddFoundationAgentApi();
            if (invalid == "duplicate") services.AddScoped<IAgentPrincipalSource, CustomPrincipalSource>();
            else services.Replace(new ServiceDescriptor(typeof(IAgentPrincipalSource), typeof(CustomPrincipalSource),
                invalid == "singleton" ? ServiceLifetime.Singleton : ServiceLifetime.Transient));
        });
        using var scope = root.CreateScope();
        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
        Assert.Contains(nameof(IAgentPrincipalSource), error.Message);
        Assert.Contains(invalid == "duplicate" ? "at most one" : "must be scoped", error.Message);
    }

    [Fact]
    public void Copied_root_principal_is_preserved_while_validation_binds_to_the_child_collection()
    {
        var parent = new ServiceCollection();
        parent.AddLogging();
        new EventsFeature().ConfigureServices(parent);
        parent.AddFoundationAgentAbstractions();
        parent.AddScoped<IAgentPrincipalSource, CustomPrincipalSource>();
        var originalParent = parent.ToArray();
        IServiceCollection child = new ServiceCollection();
        foreach (var registration in parent) child.Add(registration);
        child.AddFoundationAgentAbstractions();
        child.AddFoundationAgentApi();
        child.AddFoundationAgentAbstractions();
        using (var clean = child.BuildServiceProvider())
        using (var scope = clean.CreateScope())
        {
            Assert.IsType<CustomPrincipalSource>(Assert.Single(scope.ServiceProvider.GetServices<IAgentPrincipalSource>()));
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
        }
        child.AddScoped<IAgentPrincipalSource, CustomPrincipalSource>();
        using var invalid = child.BuildServiceProvider();
        using var invalidScope = invalid.CreateScope();
        Assert.Contains(nameof(IAgentPrincipalSource), Assert.Throws<InvalidOperationException>(() =>
            invalidScope.ServiceProvider.GetRequiredService<IAgentToolInvoker>()).Message);
        Assert.Equal(originalParent, parent.ToArray());
        using var validParent = parent.BuildServiceProvider();
        using var parentScope = validParent.CreateScope();
        Assert.NotNull(parentScope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
    }

    [Fact]
    public void Http_source_reads_genuine_current_principals_in_separate_request_scopes_without_capturing_them()
    {
        using var root = AgentTestServices.Create(services => services.AddFoundationAgentApi());
        var accessor = root.GetRequiredService<IHttpContextAccessor>();
        using var first = root.CreateScope();
        using var second = root.CreateScope();
        var firstSource = first.ServiceProvider.GetRequiredService<IAgentPrincipalSource>();
        var secondSource = second.ServiceProvider.GetRequiredService<IAgentPrincipalSource>();
        var firstPrincipal = Principal("actor-1", "tenant-1");
        var secondPrincipal = Principal("actor-2", "tenant-2");
        Assert.NotSame(firstSource, secondSource);
        try
        {
            accessor.HttpContext = new DefaultHttpContext { RequestServices = first.ServiceProvider, User = firstPrincipal };
            Assert.Same(firstPrincipal, firstSource.GetCurrent("actor-1", "tenant-1"));
            accessor.HttpContext = new DefaultHttpContext { RequestServices = second.ServiceProvider, User = secondPrincipal };
            Assert.Same(secondPrincipal, secondSource.GetCurrent("actor-2", "tenant-2"));
            Assert.Null(firstSource.GetCurrent("actor-1", "tenant-1"));
            Assert.Same(secondPrincipal, firstSource.GetCurrent("actor-2", "tenant-2"));
            accessor.HttpContext = null;
            Assert.Null(secondSource.GetCurrent("actor-2", "tenant-2"));
        }
        finally { accessor.HttpContext = null; }
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("unauthenticated")]
    [InlineData("missing-actor")]
    [InlineData("missing-tenant")]
    [InlineData("actor-case")]
    [InlineData("tenant-case")]
    public void Http_principal_source_fails_closed_for_missing_or_inexact_identity(string scenario)
    {
        using var root = AgentTestServices.Create(services => services.AddFoundationAgentApi());
        using var request = root.CreateScope();
        var accessor = root.GetRequiredService<IHttpContextAccessor>();
        List<Claim> claims = [];
        if (scenario != "missing-actor") claims.Add(new(ClaimTypes.NameIdentifier, "actor-1"));
        if (scenario != "missing-tenant") claims.Add(new("elsa.identity.tenant_id", "tenant-1"));
        try
        {
            accessor.HttpContext = scenario == "missing-context" ? null : new DefaultHttpContext
            {
                RequestServices = request.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "unauthenticated" ? null : "test"))
            };
            Assert.Null(request.ServiceProvider.GetRequiredService<IAgentPrincipalSource>().GetCurrent(
                scenario == "actor-case" ? "ACTOR-1" : "actor-1", scenario == "tenant-case" ? "TENANT-1" : "tenant-1"));
        }
        finally { accessor.HttpContext = null; }
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier, "elsa.identity.tenant_id")]
    [InlineData("sub", "tenant_id")]
    [InlineData(ClaimTypes.Name, "tenant_id")]
    public void Http_principal_source_preserves_endpoint_claim_fallbacks(string actorClaim, string tenantClaim)
    {
        using var root = AgentTestServices.Create(services => services.AddFoundationAgentApi());
        using var request = root.CreateScope();
        var accessor = root.GetRequiredService<IHttpContextAccessor>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(actorClaim, "actor-1"), new(tenantClaim, "tenant-1")], "test"));
        try
        {
            accessor.HttpContext = new DefaultHttpContext { RequestServices = request.ServiceProvider, User = principal };
            Assert.Same(principal, request.ServiceProvider.GetRequiredService<IAgentPrincipalSource>().GetCurrent("actor-1", "tenant-1"));
        }
        finally { accessor.HttpContext = null; }
    }

    private static ClaimsPrincipal Principal(string actor, string tenant) => new(new ClaimsIdentity([
        new(ClaimTypes.NameIdentifier, actor), new("elsa.identity.tenant_id", tenant),
        new("sub", "shadow-actor"), new("tenant_id", "shadow-tenant")], "test"));

    private sealed class CustomPrincipalSource : IAgentPrincipalSource
    {
        public ClaimsPrincipal? GetCurrent(string actorId, string tenantId) => null;
    }

    [ShellFeature("AgentAmbiguousPrincipalProbe", DependsOn = new object[] { typeof(FoundationAgentApiFeature) })]
    public sealed class AmbiguousPrincipalFeature : FoundationAgentApiFeature
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddScoped<IAgentPrincipalSource, CustomPrincipalSource>();
        }
    }

    [Fact]
    public async Task Github_copilot_feature_registers_provider_facade()
    {
        using var provider = AgentTestServices.Create(services => new GitHubCopilotAgentFeature().ConfigureServices(services));
        using var scope = provider.CreateScope();
        var agentProvider = Assert.Single(scope.ServiceProvider.GetServices<IAgentProvider>());
        Assert.IsType<GitHubCopilotAgentProvider>(agentProvider);
        var diagnostics = await agentProvider.GetDiagnosticsAsync();
        Assert.Equal(AgentProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.Equal(AgentProviderRiskProfile.ReviewRequired, diagnostics.RiskProfile);
        Assert.False(diagnostics.IsAvailable);
        Assert.Contains(AgentProviderOperation.Chat, diagnostics.SupportedOperations);
        Assert.Contains(AgentProviderOperation.Streaming, diagnostics.SupportedOperations);
    }

    [Fact]
    public void Workflow_agent_feature_registers_capabilities_and_context()
    {
        using var root = AgentTestServices.Create(services => new FoundationWorkflowsAgentFeature().ConfigureServices(services));
        using var scope = root.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.Contains(provider.GetServices<IAgentCapabilityProvider>(), x => x.GetCapabilitiesAsync().AsTask().Result.Any(c => c.Id == "workflow.explain"));
        Assert.Contains(provider.GetServices<IAgentContextProvider>(), x => x.ScopeKind == "workflow");
    }

    [Fact]
    public async Task Deterministic_provider_streams_contract_events_without_external_sdk()
    {
        var provider = new DeterministicAgentProvider();
        var diagnostics = await provider.GetDiagnosticsAsync();
        var session = new AgentSession(
            "session-1",
            "Test session",
            "tenant-1",
            "actor-1",
            "conversation-1",
            DeterministicAgentProvider.Id,
            "explain",
            AgentPolicy.Default,
            AgentSessionStatus.Active,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, string>());

        Assert.Equal(AgentProviderKind.ProviderSdkBinding, diagnostics.Kind);
        Assert.Equal(AgentProviderRiskProfile.ReadOnly, diagnostics.RiskProfile);
        Assert.Contains(AgentProviderOperation.Chat, diagnostics.SupportedOperations);

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.ContinueTurnAsync(AgentTurnContext.ForMessage(session.Id, "Hello", [])))
            events.Add(item);

        // The provider yields only content; the orchestrator owns turn lifecycle (Started/StepStarted/Completed).
        var delta = Assert.Single(events);
        Assert.Equal(AgentStreamEventKind.MessageDelta, delta.Kind);
        Assert.Equal("Deterministic agent provider response.", delta.Content);
    }

    [Fact]
    public async Task Orchestrator_sends_latest_user_message_and_context_to_provider()
    {
        var provider = new CapturingAgentProvider();
        var source = new AgentTestContextSource("feature.fixture.context", "feature.fixture",
            [new("ctx-1", "feature.fixture", "resource-1", "Selected resource", "test.context", AgentContextSensitivity.Internal,
                "selection", "Selected resource summary.", null, new Dictionary<string, string>()) { ModuleId = "feature.fixture" }]);
        using var sp = AgentTestServices.Create(services =>
        {
            services.AddSingleton<IAgentProvider>(provider);
            services.AddSingleton<IAgentContextProvider>(source);
        });
        using var scope = sp.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var session = await sessions.CreateAsync(new(
            "tenant-1",
            "actor-1",
            "conversation-1",
            provider.ProviderId,
            "explain",
            "Test session",
            AgentPolicy.Default,
            new Dictionary<string, string>()));
        var collected = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, source.ScopeKind, new Dictionary<string, string>()));
        Assert.True(collected.Succeeded, collected.Error?.Message);
        var context = collected.Value!.Attachments;
        await sessions.AddMessageAsync(session.Id, new(
            AgentRole.User,
            "Explain this workflow.",
            AgentMessageStatus.Pending,
            "workflow.explain",
            context.Select(item => item.Id).ToArray(),
            context));

        await foreach (var _ in streaming.StreamAsync(session.Id))
        {
        }

        Assert.NotNull(provider.LastMessage);
        Assert.Equal("Explain this workflow.", provider.LastMessage!.Content);
        Assert.Single(provider.LastMessage.Context);
    }

    private sealed class CapturingAgentProvider : IAgentProvider
    {
        public string ProviderId => "capturing-test";

        public AgentProviderMessage? LastMessage { get; private set; }

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            LastMessage = new AgentProviderMessage(context.SessionId, context.LatestUserMessage?.Content ?? string.Empty, context.Context);
            yield return new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(
                ProviderId,
                true,
                "available",
                AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat, AgentProviderOperation.Streaming],
                AgentProviderRiskProfile.ReadOnly,
                new Dictionary<string, string>()));
    }
}
