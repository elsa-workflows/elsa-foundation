using CShells.Lifecycle;
using Elsa.Weaver.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Services;

/// <summary>Checks replacement metadata at shell activation and before default authorized paths resolve.</summary>
internal sealed class WeaverRegistrationValidator(IServiceCollection registrations) : IShellInitializer
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    // The audit sink, reader and ownership are one store's facets; a durable replacement replaces all three together.
    private static readonly Type[] AuditTrio = [typeof(IWeaverAuditSink), typeof(IWeaverAuditReader), typeof(IWeaverAuditOwnership)];

    public void Validate()
    {
        var count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IWeaverToolAuthorizer));
        if (count != 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IWeaverToolAuthorizer).FullName}' requires exactly one registration, but found {count}. Use services.Replace(...) to replace its existing implementation.");
        count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IWeaverSessionRecordStore));
        if (count > 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IWeaverSessionRecordStore).FullName}' allows at most one registration, but found {count}. Select a replacement with services.SelectWeaverSessionRecordStore(...), which replaces the existing default.");
        count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IWeaverRecoverySupport));
        if (count > 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IWeaverRecoverySupport).FullName}' allows at most one registration, but found {count}. Replace the existing default instead of adding another.");
        foreach (var contract in AuditTrio)
        {
            count = registrations.Count(descriptor => descriptor.ServiceType == contract);
            if (count > 1)
                throw new InvalidOperationException($"Agent replacement contract '{contract.FullName}' allows at most one registration, but found {count}. Select the audit sink, reader and ownership together with services.SelectWeaverAuditStore(...), which replaces the three defaults at once.");
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
        services.GetRequiredService<WeaverRegistrationValidator>().Validate();
        return ActivatorUtilities.CreateInstance<T>(services);
    }
}
