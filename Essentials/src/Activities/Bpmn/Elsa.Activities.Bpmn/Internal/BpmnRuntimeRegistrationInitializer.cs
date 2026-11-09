using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Checks the completed composition without constructing scoped adapters or their persistence stores.</summary>
internal sealed class BpmnRuntimeRegistrationInitializer(IServiceCollection registrations) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IBpmnProcessRuntime));
        if (count != 1)
            throw new InvalidOperationException($"BPMN composition requires exactly one {nameof(IBpmnProcessRuntime)} registration, but found {count}.");
        return Task.CompletedTask;
    }
}
