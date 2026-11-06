using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed record WorkflowTestEntryRegistrations(Func<ServiceDescriptor[]> Read);

internal sealed class ValidateWorkflowTestEntryComposition(WorkflowTestEntryRegistrations registrations) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IWorkflowTestEntryContextResolver)) > 1)
            throw new InvalidOperationException("Runtime permits at most one trusted test-entry context resolver.");
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestContextSeedSource)) > 1)
            throw new InvalidOperationException("Runtime permits at most one admitted test setup source.");
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestContextFrameAccess)) != 1)
            throw new InvalidOperationException("Runtime requires exactly one test frame owner.");
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestContextPreparer)) != 1)
            throw new InvalidOperationException("Runtime requires exactly one test context preparer.");
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestEffectGate)) != 1)
            throw new InvalidOperationException("Runtime requires exactly one invocation effect gate.");
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestEffectPolicySource)) > 1)
            throw new InvalidOperationException("Runtime permits at most one retained test effect policy source.");
        if (registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestPersistenceBoundary)) > 1 ||
            registrations.Read().Count(descriptor => descriptor.ServiceType == typeof(IRuntimeTestExecutionRoutingBoundary)) > 1)
            throw new InvalidOperationException("Runtime permits at most one physical test persistence boundary and routing boundary.");
        return Task.CompletedTask;
    }
}
