using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Invokes the owning module's cleanup hook before dropping one prepared target projection.</summary>
internal sealed class TestRunTargetCleaner(
    ITestRunTargetStore targets,
    IWorkflowExecutableStore executables,
    IEnumerable<ITestRunTriggerDriver> drivers,
    ITestRunRootCanceller? rootCanceller = null) : ITestRunTargetCleaner
{
    public async ValueTask StopAsync(string testRunId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        var target = await targets.FindAsync(testRunId, cancellationToken);
        if (target is null)
            return;

        target = await targets.MarkStoppedAsync(testRunId, cancellationToken) ?? target;
        if (rootCanceller is not null)
            await rootCanceller.RequestCancelAsync(testRunId, target.WorkflowExecutionId, target.Scope.Partition, cancellationToken);

        var executable = await executables.FindAsync(target.Executable.ArtifactId, cancellationToken);
        if (executable is not null && executable.NodesById.TryGetValue(target.Binding.ExecutableNodeId, out var node))
        {
            var context = new TestRunTriggerContext(executable, target.ProviderId, node, target.Binding);
            var matchingDrivers = drivers.Where(driver => StringComparer.Ordinal.Equals(driver.DriverId, target.DriverId)).ToArray();
            if (matchingDrivers.Length == 1)
            {
                await matchingDrivers[0].StopAsync(
                    new TestRunTriggerStopContext(context, target.Scope, target.TestRunId, target.ModeId, target.DriverState),
                    cancellationToken);
            }
        }

        await targets.RemoveAsync(testRunId, cancellationToken);
    }
}
