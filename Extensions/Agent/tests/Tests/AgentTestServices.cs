using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>Real named-event/default composition; tests supply authority explicitly instead of weakening defaults.</summary>
internal static class AgentTestServices
{
    public static ServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddFoundationAgentAbstractions();
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, AllowTestAgentToolAuthorizer>());
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

internal sealed class AllowTestAgentToolAuthorizer : IAgentToolAuthorizer
{
    public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
        AgentResourceTarget? target, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentPolicyDecision(true, []));
}
