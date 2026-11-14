using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Services.Values;

internal sealed class ValidateRuntimeTestEntryComposition(RuntimeTestContextInspectionRegistrations registrations) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = registrations.Read();
        if (selected.Count(x => x.ServiceType == typeof(IRuntimeTestContextPreparer)) != 1)
            throw new InvalidOperationException("Runtime requires exactly one trusted test context preparer.");
        if (selected.Count(x => x.ServiceType == typeof(IRuntimeTestContextFrameAccess)) != 1)
            throw new InvalidOperationException("Runtime requires exactly one test context frame owner.");
        foreach (var contract in new[] { typeof(IWorkflowTestEntryContextResolver), typeof(IRuntimeTestContextSeedSource),
                     typeof(IRuntimeSelectedStartTargetProfile), typeof(IRuntimeTestPersistenceBoundary), typeof(IRuntimeTestExecutionRoutingBoundary) })
            if (selected.Count(x => x.ServiceType == contract) > 1)
                throw new InvalidOperationException($"Runtime test entry requires at most one {contract.Name} registration.");
        return Task.CompletedTask;
    }
}
