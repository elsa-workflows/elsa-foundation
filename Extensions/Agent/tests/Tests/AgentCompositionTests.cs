using CShells;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent.Anthropic;
using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.Workflows;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentCompositionTests
{
    private const string ShellName = "agent-composition";
    private const string DefaultFeatureId = "FoundationAgentAbstractions";

    private static readonly Type[] DefaultContracts =
    [
        typeof(IAgentPolicyEvaluator),
        typeof(IAgentContextSanitizer),
        typeof(IAgentContextCollector),
        typeof(IAgentCapabilityCatalog),
        typeof(IAgentSessionService),
        typeof(IAgentProposalService),
        typeof(IAgentActionProposalExecutor),
        typeof(IAgentAuditSink),
        typeof(IAgentAuditReader),
        typeof(IAgentFeedbackService),
        typeof(IAgentToolRegistry),
        typeof(IAgentTurnRegistry),
        typeof(IAgentTurnStateStore),
        typeof(IAgentToolInvoker),
        typeof(IAgentStreamingService),
        typeof(IAgentProviderRegistry)
    ];

    [Theory]
    [InlineData(typeof(FoundationAgentApiFeature))]
    [InlineData(typeof(FoundationWorkflowsAgentFeature))]
    [InlineData(typeof(GitHubCopilotAgentFeature))]
    [InlineData(typeof(AnthropicAgentFeature))]
    public async Task A_leaf_discovers_and_activates_its_default_feature_through_the_type_dependency(Type leaf)
    {
        // Only the leaf and Events assemblies are supplied. The default Agent assembly must be
        // discovered through the leaf's Type dependency, as must the API capability feature for
        // the API leaf. Dependencies are not explicitly enabled.
        await using var root = BuildRoot([leaf]);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        var descriptor = Assert.Single(services.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>(),
            feature => feature.Id == DefaultFeatureId);
        Assert.Equal(typeof(FoundationAgentAbstractionsFeature), descriptor.StartupType);

        var enabled = services.GetRequiredService<ShellSettings>().EnabledFeatures.ToArray();
        Assert.Contains(DefaultFeatureId, enabled);
        Assert.Contains("Events", enabled);
        Assert.True(Array.IndexOf(enabled, "Events") < Array.IndexOf(enabled, DefaultFeatureId));
        Assert.True(Array.IndexOf(enabled, DefaultFeatureId) < Array.IndexOf(enabled, ShellBuilder.ResolveFeatureName(leaf)));

        if (leaf == typeof(FoundationAgentApiFeature))
        {
            var capabilities = Assert.Single(services.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>(),
                feature => feature.Id == "ApiCapabilities");
            Assert.Equal(typeof(ApiCapabilitiesFeature), capabilities.StartupType);
            Assert.Contains("ApiCapabilities", enabled);
            Assert.True(Array.IndexOf(enabled, "ApiCapabilities") < Array.IndexOf(enabled, ShellBuilder.ResolveFeatureName(leaf)));
        }

        AssertSingleDefaults(services);
        Assert.NotNull(services.GetRequiredService<IInlineEventPublisher>());
    }

    [Fact]
    public async Task Missing_events_discovery_refuses_shell_activation_with_the_named_dependency()
    {
        await using var root = BuildRoot([typeof(FoundationWorkflowsAgentFeature)], includeEvents: false);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        var missing = Assert.Single(ExceptionChain(failure).OfType<FeatureNotFoundException>());
        Assert.Equal("Events", missing.FeatureName);
        Assert.Equal(DefaultFeatureId, missing.DependentFeatureName);
    }

    [Theory]
    [InlineData(typeof(GitHubCopilotAgentFeature))]
    [InlineData(typeof(AnthropicAgentFeature))]
    public async Task Explicit_base_and_shared_leaves_keep_one_default_and_one_aggregator_per_event(Type providerFeature)
    {
        await using var root = BuildRoot(
            [typeof(FoundationAgentApiFeature), typeof(FoundationWorkflowsAgentFeature), providerFeature],
            includeBase: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        AssertSingleDefaults(services);
        Assert.Single(services.GetServices<IAgentProvider>());
        Assert.NotNull(services.GetRequiredService<IAgentProviderRegistry>().Active);
        AssertSingleContributionHandlers(services);
    }

    [Fact]
    public async Task Both_provider_leaves_preserve_single_defaults_and_refuse_the_provider_conflict()
    {
        await using var root = BuildRoot(
            [typeof(FoundationAgentApiFeature), typeof(FoundationWorkflowsAgentFeature),
                typeof(GitHubCopilotAgentFeature), typeof(AnthropicAgentFeature)],
            includeBase: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;

        // Provider alternatives cannot form one active harness. All independent generic defaults
        // still resolve once; the registry and its streaming consumer must report the real conflict.
        AssertSingleDefaults(services, hasProviderConflict: true);
        Assert.Equal(2, services.GetServices<IAgentProvider>().Count());
        var failure = Assert.Throws<AgentHarnessConflictException>(() =>
            services.GetRequiredService<IAgentProviderRegistry>());
        Assert.Equal(2, failure.ProviderIds.Count);
        Assert.Throws<AgentHarnessConflictException>(() => services.GetRequiredService<IAgentStreamingService>());
        AssertSingleContributionHandlers(services);
    }

    private static void AssertSingleDefaults(IServiceProvider services, bool hasProviderConflict = false)
    {
        foreach (var contract in DefaultContracts)
        {
            if (hasProviderConflict && (contract == typeof(IAgentProviderRegistry) || contract == typeof(IAgentStreamingService)))
                continue;

            Assert.NotNull(Assert.Single(services.GetServices(contract)));
        }
    }

    private static void AssertSingleContributionHandlers(IServiceProvider services)
    {
        Assert.Single(services.GetServices<IEventHandler<AgentContextCollecting>>());
        Assert.Single(services.GetServices<IEventHandler<AgentCapabilitiesCollecting>>());
        Assert.Single(services.GetServices<IEventHandler<AgentToolsCollecting>>());

        var handlers = services.GetServices<IEventHandler>().Select(handler => handler.GetType()).ToArray();
        Assert.Equal(handlers.Length, handlers.Distinct().Count());
    }

    private static ServiceProvider BuildRoot(Type[] leaves, bool includeEvents = true, bool includeBase = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var assemblies = leaves.Select(feature => feature.Assembly).Distinct().ToList();
        if (includeEvents)
            assemblies.Add(typeof(EventsFeature).Assembly);

        services.AddCShells(builder => builder
            .WithAssemblies(assemblies.ToArray())
            .AddShell(ShellName, shell =>
            {
                if (includeBase)
                    shell.WithFeature<FoundationAgentAbstractionsFeature>();
                foreach (var leaf in leaves)
                    shell.WithFeature(leaf);
            }));
        return services.BuildServiceProvider();
    }

    private static IEnumerable<Exception> ExceptionChain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
            yield return exception;
    }
}
