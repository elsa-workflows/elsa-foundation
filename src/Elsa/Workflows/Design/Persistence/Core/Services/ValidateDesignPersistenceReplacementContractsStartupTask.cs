using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.Core.Services;

/// <summary>Fails startup unless every replacement contract has exactly one registered implementation.</summary>
public sealed class ValidateDesignPersistenceReplacementContractsStartupTask(IServiceCollection services) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invalidRegistrations = DesignPersistenceBackend.ReplacementContractTypes
            .Select(serviceType => (serviceType, Count: services.Count(descriptor => descriptor.ServiceType == serviceType)))
            .Where(item => item.Count != 1)
            .Select(item => $"{item.serviceType.Name} ({item.Count} registrations)")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        if (invalidRegistrations.Length > 0)
            throw new InvalidOperationException($"Workflow-design replacement contracts must have exactly one implementation; invalid registrations: {string.Join(", ", invalidRegistrations)}.");

        return Task.CompletedTask;
    }
}
