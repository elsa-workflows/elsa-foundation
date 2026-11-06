using CShells.Lifecycle;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Services;

/// <summary>Checks replacement metadata at shell activation and before default authorized paths resolve.</summary>
internal sealed class AgentRegistrationValidator(IServiceCollection registrations) : IShellInitializer
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    // The audit sink, reader and ownership are one store's facets; a durable replacement replaces all three together.
    private static readonly Type[] AuditTrio = [typeof(IAgentAuditSink), typeof(IAgentAuditReader), typeof(IAgentAuditOwnership)];

    public void Validate() => _ = GetEffectsSelection();

    internal AgentEffectsSelection? GetEffectsSelection()
    {
        var support = registrations.Where(descriptor => descriptor.ServiceType == typeof(IAgentSupportSource)
            && !descriptor.IsKeyedService).ToArray();
        if (support.Length > 1 || support.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped))
            throw new InvalidOperationException("Agent replacement contract 'IAgentSupportSource' requires at most one scoped registration. Use services.Replace(...) to replace its existing implementation.");
        var principals = registrations.Where(descriptor => descriptor.ServiceType == typeof(IAgentPrincipalSource)
            && !descriptor.IsKeyedService).ToArray();
        if (principals.Length > 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IAgentPrincipalSource).FullName}' allows at most one registration, but found {principals.Length}. Use services.Replace(...) to replace its existing implementation.");
        if (principals.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped))
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IAgentPrincipalSource).FullName}' must be scoped so it reads the genuine current principal for each operation.");
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
        return AgentEffectsAdmission.ValidateSelection(registrations);
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
