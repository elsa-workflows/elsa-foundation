using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.Workflows.Services;

namespace Elsa.Agent.Tests;

public sealed class AgentProviderRegistryTests
{
    [Fact]
    public void No_provider_means_no_active_harness()
    {
        var registry = new DefaultAgentProviderRegistry([]);

        Assert.Null(registry.Active);
    }

    [Fact]
    public void One_provider_is_the_active_harness()
    {
        var provider = new DeterministicAgentProvider();
        var registry = new DefaultAgentProviderRegistry([provider]);

        Assert.Same(provider, registry.Active);
    }

    [Fact]
    public void More_than_one_provider_fails_fast_with_both_ids()
    {
        var ex = Assert.Throws<AgentHarnessConflictException>(() =>
            new DefaultAgentProviderRegistry([new DeterministicAgentProvider(), new DeterministicWorkflowAgentProvider()]));

        Assert.Contains(DeterministicAgentProvider.Id, ex.ProviderIds);
        Assert.Contains(DeterministicWorkflowAgentProvider.Id, ex.ProviderIds);
        Assert.Contains("Enable exactly one", ex.Message);
    }
}
