using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn.Interchange.Services;

internal sealed class BpmnInterchangeRegistrationInitializer(IServiceCollection registrations) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var contract in new[] { typeof(IBpmnDocumentImporter), typeof(IBpmnDocumentExporter), typeof(IBpmnWorkBinder) })
        {
            var count = registrations.Count(descriptor => descriptor.ServiceType == contract);
            if (count != 1)
                throw new InvalidOperationException($"BPMN interchange requires exactly one {contract.Name} registration, but found {count}.");
        }
        return Task.CompletedTask;
    }
}
