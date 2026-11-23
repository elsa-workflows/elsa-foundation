using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Rejects ambiguous replacement registrations, including additions after feature configuration.</summary>
internal sealed class StatePreparationStartupTask(IServiceCollection services) : IStartupTask
{
    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(descriptor => descriptor.ServiceType == typeof(IWorkflowStateChangePreparer)) > 1)
            throw new InvalidOperationException("IWorkflowStateChangePreparer has multiple registrations; configure one replacement.");
    }

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(services);
        return Task.CompletedTask;
    }
}
