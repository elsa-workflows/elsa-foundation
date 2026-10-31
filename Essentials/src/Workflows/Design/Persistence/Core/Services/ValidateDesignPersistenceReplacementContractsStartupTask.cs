using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.Core.Services;

/// <summary>Validates one owner for mandatory replacement contracts and at most one for optional ports.</summary>
public sealed class ValidateDesignPersistenceReplacementContractsStartupTask(IServiceCollection services) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invalidRegistrations = DesignPersistenceBackend.ReplacementContractTypes
            .Select(serviceType => (serviceType, Count: services.Count(descriptor => descriptor.ServiceType == serviceType)))
            // Library support is optional for existing design backends. Keeping its
            // contract in the ownership set still rejects an implicit second owner.
            .Where(item => item.serviceType == typeof(IWorkflowLibraryStore) ? item.Count > 1 : item.Count != 1)
            .Select(item => $"{item.serviceType.Name} ({item.Count} registrations)")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        // Template support is optional, but a selected port still has exactly one owner.
        if (services.Count(descriptor => descriptor.ServiceType == typeof(IWorkflowTemplateStore)) > 1)
            throw new InvalidOperationException("IWorkflowTemplateStore has multiple registrations; select one explicit template persistence implementation.");

        if (invalidRegistrations.Length > 0)
            throw new InvalidOperationException($"Workflow-design replacement contracts must have exactly one implementation; invalid registrations: {string.Join(", ", invalidRegistrations)}.");

        return Task.CompletedTask;
    }
}
