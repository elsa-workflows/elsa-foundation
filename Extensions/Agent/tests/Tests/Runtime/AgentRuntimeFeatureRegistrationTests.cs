using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Runtime;

public sealed class AgentRuntimeFeatureRegistrationTests
{
    [Fact]
    public void Runtime_registers_the_scoped_invocation_inspector_as_a_replaceable_default()
    {
        var services = new ServiceCollection();
        new AgentRuntimeFeature().ConfigureServices(services);
        var inspector = Assert.Single(services, x => x.ServiceType == typeof(IAgentInvocationInspector));
        Assert.Equal(ServiceLifetime.Scoped, inspector.Lifetime);
        Assert.Equal(typeof(AgentInvocationInspector), inspector.ImplementationType);

        var replaced = new ServiceCollection();
        replaced.AddScoped<IAgentInvocationInspector, HostInspector>();
        new AgentRuntimeFeature().ConfigureServices(replaced);
        Assert.Equal(typeof(HostInspector), Assert.Single(replaced, x => x.ServiceType == typeof(IAgentInvocationInspector)).ImplementationType);
    }

    [Fact]
    public void Runtime_projects_the_state_view_of_every_agent_node_by_its_descriptor_kind()
    {
        var services = new ServiceCollection();
        new AgentRuntimeFeature().ConfigureServices(services);

        var projector = (Elsa.Workflows.Runtime.Core.Contracts.IActivityStateViewProjector)Activator.CreateInstance(
            Assert.Single(services, x => x.ServiceType == typeof(Elsa.Workflows.Runtime.Core.Contracts.IActivityStateViewProjector)).ImplementationType!)!;
        Assert.Equal(("elsa.agent", "elsa.agent.invocation", 1), (projector.DescriptorKind, projector.ViewKind, projector.ViewVersion));
        Assert.Null(projector.ActivityTypeWithoutState);
    }

    [Fact]
    public async Task Runtime_startup_refuses_a_second_invocation_inspector_instead_of_letting_the_last_one_win()
    {
        var services = new ServiceCollection();
        new AgentRuntimeFeature().ConfigureServices(services);
        new AgentInMemoryJournalFeature().ConfigureServices(services);
        var check = (AgentRegistrationStartupTask)Assert.Single(services, x => x.ImplementationInstance is AgentRegistrationStartupTask).ImplementationInstance!;
        await check.ExecuteAsync(CancellationToken.None);

        services.AddScoped<IAgentInvocationInspector, HostInspector>();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => check.ExecuteAsync(CancellationToken.None));
        Assert.Contains(nameof(IAgentInvocationInspector), refusal.Message, StringComparison.Ordinal);
    }

    private sealed class HostInspector : IAgentInvocationInspector
    {
        public ValueTask<AgentInvocationEvidence?> InspectAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AgentInvocationEvidence?>(null);
    }
}
