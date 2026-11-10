using CShells.Lifecycle;
using Elsa.Weaver.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Services;

/// <summary>Checks replacement metadata at shell activation and before default authorized paths resolve.</summary>
internal sealed class AgentRegistrationValidator(IServiceCollection registrations) : IShellInitializer
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    // The audit sink, reader and ownership are one store's facets; a durable replacement replaces all three together.
    private static readonly Type[] AuditTrio = [typeof(IAgentAuditSink), typeof(IAgentAuditReader), typeof(IAgentAuditOwnership)];

    public void Validate()
    {
        var count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IAgentToolAuthorizer));
        if (count != 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IAgentToolAuthorizer).FullName}' requires exactly one registration, but found {count}. Use services.Replace(...) to replace its existing implementation.");
        count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IAgentSessionRecordStore));
        if (count > 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IAgentSessionRecordStore).FullName}' allows at most one registration, but found {count}. Select a replacement with services.SelectAgentSessionRecordStore(...), which replaces the existing default.");
        count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IAgentRecoverySupport));
        if (count > 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IAgentRecoverySupport).FullName}' allows at most one registration, but found {count}. Replace the existing default instead of adding another.");
        foreach (var contract in AuditTrio)
        {
            count = registrations.Count(descriptor => descriptor.ServiceType == contract);
            if (count > 1)
                throw new InvalidOperationException($"Agent replacement contract '{contract.FullName}' allows at most one registration, but found {count}. Select the audit sink, reader and ownership together with services.SelectAgentAuditStore(...), which replaces the three defaults at once.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate();
        return Task.CompletedTask;
    }

    internal static T Create<T>(IServiceProvider services) where T : class
    {
        services.GetRequiredService<AgentRegistrationValidator>().Validate();
        return ActivatorUtilities.CreateInstance<T>(services);
    }
}
