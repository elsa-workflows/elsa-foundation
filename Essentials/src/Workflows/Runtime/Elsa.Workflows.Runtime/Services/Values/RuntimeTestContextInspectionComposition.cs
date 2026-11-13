using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Values;

internal sealed record RuntimeTestContextInspectionRegistrations(Func<ServiceDescriptor[]> Read);

internal sealed class ValidateRuntimeTestContextInspectionComposition(RuntimeTestContextInspectionRegistrations registrations) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registrations.Read().Count(x => x.ServiceType == typeof(IRuntimeTestContextInspector)) != 1)
            throw new InvalidOperationException("Runtime requires exactly one test context inspector.");
        return Task.CompletedTask;
    }
}
