using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

/// <summary>Diagnoses conflicting model ownership before delivery inspection is available.</summary>
internal sealed class ValidateTriggerOutcomeProjectors(IEnumerable<IRuntimeModelExecutionOutcomeProjector> projectors) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owners = new Dictionary<string, IRuntimeModelExecutionOutcomeProjector>(StringComparer.Ordinal);
        foreach (var projector in projectors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projector.ModelKey);
            if (!owners.TryAdd(projector.ModelKey, projector))
                throw new InvalidOperationException($"Model outcome projectors '{owners[projector.ModelKey].GetType().FullName}' and '{projector.GetType().FullName}' both own model '{projector.ModelKey}'; at most one may.");
        }
        return Task.CompletedTask;
    }
}
